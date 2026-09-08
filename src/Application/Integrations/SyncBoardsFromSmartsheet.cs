using Application.Abstractions;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Integrations;

/// <summary>
/// Pulls every Smartsheet-linked board and writes the figures back.
///
/// The Smartsheet twin of <see cref="SyncBoardsFromJiraCommand"/>, and deliberately the
/// same shape: one code path serves both the background worker and the admin's "Sync now"
/// button, so what the button does and what the schedule does cannot drift apart.
///
/// Authorisation is the caller's job here, as it is for Jira: the background worker runs
/// with no signed-in user, so the handler cannot ask <c>IBoardAuthorizer</c> who it is.
/// The HTTP route that reaches this is admin-gated in the controller.
/// </summary>
public sealed record SyncBoardsFromSmartsheetCommand(
    string RequestedBy = "Smartsheet sync",
    bool RespectAutoApply = true) : IRequest<SmartsheetSyncReport>;

public sealed record SmartsheetSyncReport(
    bool Ran,
    string Message,
    int BoardsConsidered,
    int BoardsUpdated,
    int BoardsUnreachable,
    IReadOnlyList<string> Details)
{
    public static SmartsheetSyncReport Skipped(string why) =>
        new(false, why, 0, 0, 0, Array.Empty<string>());
}

public sealed class SyncBoardsFromSmartsheetCommandHandler(
    IAppDbContext db,
    ISmartsheetClient smartsheet,
    ISmartsheetSettingsService settings,
    IBoardNotifier notifier,
    ILogger<SyncBoardsFromSmartsheetCommandHandler> logger)
    : IRequestHandler<SyncBoardsFromSmartsheetCommand, SmartsheetSyncReport>
{
    public async Task<SmartsheetSyncReport> Handle(
        SyncBoardsFromSmartsheetCommand request, CancellationToken cancellationToken)
    {
        var connection = await settings.GetAsync(cancellationToken);

        if (!await smartsheet.IsEnabledAsync(cancellationToken))
        {
            return SmartsheetSyncReport.Skipped("Smartsheet is not configured.");
        }

        if (request.RespectAutoApply && !connection.AutoApply)
        {
            // The default. Boards still show a Smartsheet suggestion in the editor;
            // nothing is written until a human accepts it.
            return SmartsheetSyncReport.Skipped(
                "Auto-apply is off, so boards are left for their owners to update.");
        }

        var boards = await db.Boards
            .Where(b => b.SmartsheetSheetId != null && b.SmartsheetSheetId != "")
            .ToListAsync(cancellationToken);

        if (boards.Count == 0)
        {
            return new SmartsheetSyncReport(true, "No boards are linked to a sheet.",
                0, 0, 0, Array.Empty<string>());
        }

        var unreachable = 0;
        var details = new List<string>();
        var changedBoardIds = new List<Guid>();

        foreach (var board in boards)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await smartsheet.GetSnapshotAsync(
                board.SmartsheetSheetId!, cancellationToken);

            if (snapshot is null)
            {
                // One unreachable sheet must not abort the others.
                unreachable++;
                details.Add($"{board.Title}: Smartsheet returned nothing for sheet " +
                            $"{board.SmartsheetSheetId}.");
                continue;
            }

            // Smartsheet has no sprint concept, so the sprint is left exactly as the
            // Product Owner set it rather than being blanked by an integration that
            // cannot know it.
            var changes = board.ApplyJiraSnapshot(
                null, snapshot.SuggestedProgressPercent, snapshot.SuggestedStatus);

            if (changes.Count == 0) continue;

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

        // Only boards that actually changed — an unchanged board should not make every
        // open editor flash a "board updated" banner every interval.
        foreach (var boardId in changedBoardIds)
        {
            await notifier.BoardUpdatedAsync(
                boardId, new { source = "smartsheet" }, cancellationToken);
        }

        var message = $"Checked {boards.Count} board(s): {changedBoardIds.Count} updated" +
                      (unreachable > 0 ? $", {unreachable} unreachable." : ".");

        await settings.RecordSyncAsync(message, cancellationToken);
        logger.LogInformation("Smartsheet sync by {By}. {Message}", request.RequestedBy, message);

        return new SmartsheetSyncReport(
            true, message, boards.Count, changedBoardIds.Count, unreachable, details);
    }
}

// ---------------------------------------------------------------------------
// Suggestion and connection test
// ---------------------------------------------------------------------------

/// <summary>
/// Reads one board's sheet and returns a **suggestion**. A query, not a command: it never
/// writes to the board. The Product Owner sees the pulled numbers and decides.
/// </summary>
public sealed record GetSmartsheetSuggestionQuery(Guid BoardId) : IRequest<SmartsheetSuggestionDto>;

public sealed record SmartsheetSuggestionDto(
    bool Available,
    string? Reason,
    string? SheetName,
    int DoneRows,
    int TotalRows,
    int BlockedRows,
    int SuggestedProgressPercent,
    bool ProgressFromColumn,
    int SuggestedStatus,
    string SuggestedStatusLabel,
    string SuggestedStatusColor,
    string Rationale,
    int CurrentProgressPercent,
    int CurrentStatus)
{
    public static SmartsheetSuggestionDto Unavailable(string reason) => new(
        false, reason, null, 0, 0, 0, 0, false, 0, string.Empty, string.Empty,
        string.Empty, 0, 0);
}

public sealed class GetSmartsheetSuggestionQueryHandler(
    IAppDbContext db, ISmartsheetClient smartsheet)
    : IRequestHandler<GetSmartsheetSuggestionQuery, SmartsheetSuggestionDto>
{
    public async Task<SmartsheetSuggestionDto> Handle(
        GetSmartsheetSuggestionQuery request, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.BoardId} was not found.");

        if (!await smartsheet.IsEnabledAsync(cancellationToken))
        {
            return SmartsheetSuggestionDto.Unavailable(
                "Smartsheet is not configured for this deployment.");
        }

        if (string.IsNullOrWhiteSpace(board.SmartsheetSheetId))
        {
            return SmartsheetSuggestionDto.Unavailable(
                "This board has no Smartsheet sheet id. Add one to enable sync.");
        }

        var snapshot = await smartsheet.GetSnapshotAsync(board.SmartsheetSheetId, cancellationToken);

        if (snapshot is null)
        {
            return SmartsheetSuggestionDto.Unavailable(
                $"Smartsheet returned nothing for sheet '{board.SmartsheetSheetId}'.");
        }

        return new SmartsheetSuggestionDto(
            true,
            null,
            snapshot.SheetName,
            snapshot.DoneRows,
            snapshot.TotalRows,
            snapshot.BlockedRows,
            snapshot.SuggestedProgressPercent,
            snapshot.ProgressFromColumn,
            (int)snapshot.SuggestedStatus,
            Domain.Enums.BoardStatusMetadata.Label(snapshot.SuggestedStatus),
            Domain.Enums.BoardStatusMetadata.Color(snapshot.SuggestedStatus),
            snapshot.Rationale,
            board.ProgressPercent,
            (int)board.Status);
    }
}

/// <summary>
/// Whether Smartsheet is wired up, and whether the token actually works. Makes a real
/// call, so an admin can tell "not configured" from "configured but the token is wrong".
/// </summary>
public sealed record GetSmartsheetConnectionQuery(string? ProbeSheetId = null)
    : IRequest<SmartsheetConnectionDto>;

public sealed record SmartsheetConnectionDto(
    bool Enabled,
    bool Reachable,
    string Message,
    string? ProbedSheetId,
    int? RowsSeen);

public sealed class GetSmartsheetConnectionQueryHandler(
    ISmartsheetClient smartsheet, IBoardAuthorizer authorizer)
    : IRequestHandler<GetSmartsheetConnectionQuery, SmartsheetConnectionDto>
{
    public async Task<SmartsheetConnectionDto> Handle(
        GetSmartsheetConnectionQuery request, CancellationToken cancellationToken)
    {
        // Probing an external system with our credentials is an administrative action.
        authorizer.EnsureIsAdmin();

        if (!await smartsheet.IsEnabledAsync(cancellationToken))
        {
            return new SmartsheetConnectionDto(
                false, false,
                "Smartsheet is not configured. Add an access token and enable the connection.",
                null, null);
        }

        if (string.IsNullOrWhiteSpace(request.ProbeSheetId))
        {
            return new SmartsheetConnectionDto(
                true, false,
                "Smartsheet is configured. Supply a sheet id to test the token against it.",
                null, null);
        }

        var snapshot = await smartsheet.GetSnapshotAsync(request.ProbeSheetId, cancellationToken);

        return snapshot is null
            ? new SmartsheetConnectionDto(
                true, false,
                $"Smartsheet is configured but returned nothing for sheet " +
                $"'{request.ProbeSheetId}'. Check the sheet id, that the token's account " +
                "can open it, and that the token is still valid.",
                request.ProbeSheetId, null)
            : new SmartsheetConnectionDto(
                true, true,
                $"Connected. Read {snapshot.TotalRows} row(s) from \"{snapshot.SheetName}\".",
                request.ProbeSheetId, snapshot.TotalRows);
    }
}
