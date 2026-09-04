using Application.Abstractions;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Integrations;

/// <summary>
/// Pulls every Jira-linked board and writes the figures back.
///
/// One code path serves both callers — the background worker on its interval and the
/// admin's "Sync now" button — so what the button does and what the schedule does can
/// never drift apart.
///
/// Authorisation is the caller's job here, and deliberately so: the background worker runs
/// with no signed-in user at all, so the handler cannot ask <c>IBoardAuthorizer</c> who it
/// is. The HTTP route that reaches this is admin-gated in the controller.
/// </summary>
/// <param name="RequestedBy">Shown in the audit trail — "Jira sync" or an admin's name.</param>
/// <param name="RespectAutoApply">
/// True for the scheduled run: it declines to write unless auto-apply is switched on.
/// False for the admin pressing the button, which is an explicit instruction to write.
/// </param>
public sealed record SyncBoardsFromJiraCommand(
    string RequestedBy = "Jira sync",
    bool RespectAutoApply = true) : IRequest<JiraSyncReport>;

public sealed record JiraSyncReport(
    bool Ran,
    string Message,
    int BoardsConsidered,
    int BoardsUpdated,
    int BoardsUnreachable,
    IReadOnlyList<string> Details)
{
    public static JiraSyncReport Skipped(string why) =>
        new(false, why, 0, 0, 0, Array.Empty<string>());
}

public sealed class SyncBoardsFromJiraCommandHandler(
    IAppDbContext db,
    IJiraClient jira,
    IJiraSettingsService settings,
    IBoardNotifier notifier,
    ILogger<SyncBoardsFromJiraCommandHandler> logger)
    : IRequestHandler<SyncBoardsFromJiraCommand, JiraSyncReport>
{
    public async Task<JiraSyncReport> Handle(
        SyncBoardsFromJiraCommand request, CancellationToken cancellationToken)
    {
        var connection = await settings.GetAsync(cancellationToken);

        if (!await jira.IsEnabledAsync(cancellationToken))
        {
            return JiraSyncReport.Skipped("Jira is not configured.");
        }

        if (request.RespectAutoApply && !connection.AutoApply)
        {
            // The default. Boards still show a Jira suggestion in the editor; nothing is
            // written until a human accepts it, or an admin turns auto-apply on.
            return JiraSyncReport.Skipped(
                "Auto-apply is off, so boards are left for their owners to update.");
        }

        var boards = await db.Boards
            .Where(b => b.JiraProjectKey != null && b.JiraProjectKey != "")
            .ToListAsync(cancellationToken);

        if (boards.Count == 0)
        {
            return new JiraSyncReport(true, "No boards have a Jira project key.",
                0, 0, 0, Array.Empty<string>());
        }

        var unreachable = 0;
        var details = new List<string>();
        var changedBoardIds = new List<Guid>();

        foreach (var board in boards)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await jira.GetSnapshotAsync(
                board.JiraProjectKey!, board.JiraBoardId, cancellationToken);

            if (snapshot is null)
            {
                // One unreachable project must not abort the others.
                unreachable++;
                details.Add($"{board.Title}: Jira returned nothing for {board.JiraProjectKey}.");
                continue;
            }

            var changes = board.ApplyJiraSnapshot(
                snapshot.SprintName,
                snapshot.SuggestedProgressPercent,
                snapshot.SuggestedStatus);

            if (changes.Count == 0)
            {
                continue;
            }

            foreach (var change in changes)
            {
                db.BoardAuditEntries.Add(new BoardAuditEntry(
                    board.Id, change.Field, change.OldValue, change.NewValue,
                    request.RequestedBy));
            }

            changedBoardIds.Add(board.Id);
            details.Add($"{board.Title}: {string.Join(", ", changes.Select(c => c.Field))}.");
        }

        await db.SaveChangesAsync(cancellationToken);

        // Notify after the write commits, so a viewer that refetches sees the new figures.
        // Only boards that actually changed — an unchanged board should not make every
        // open editor flash a "board updated" banner every interval.
        foreach (var boardId in changedBoardIds)
        {
            await notifier.BoardUpdatedAsync(boardId, new { source = "jira" }, cancellationToken);
        }

        var message = $"Checked {boards.Count} board(s): {changedBoardIds.Count} updated" +
                      (unreachable > 0 ? $", {unreachable} unreachable." : ".");

        await settings.RecordSyncAsync(message, cancellationToken);
        logger.LogInformation("Jira sync by {By}. {Message}", request.RequestedBy, message);

        return new JiraSyncReport(
            true, message, boards.Count, changedBoardIds.Count, unreachable, details);
    }
}
