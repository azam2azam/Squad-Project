using Application.Abstractions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Queries;

/// <summary>
/// Pulls the board's Jira project and returns a **suggestion**. Deliberately a query, not
/// a command: it never writes to the board. The Product Owner sees the pulled numbers and
/// decides whether to accept them (spec section 10).
/// </summary>
public sealed record GetJiraSuggestionQuery(Guid BoardId) : IRequest<JiraSuggestionDto>;

public sealed record JiraSuggestionDto(
    bool Available,
    string? Reason,
    string? SprintName,
    int DoneIssues,
    int TotalIssues,
    int BlockedIssues,
    int SuggestedProgressPercent,
    BoardStatus SuggestedStatus,
    string SuggestedStatusLabel,
    string SuggestedStatusColor,
    string Rationale,
    string? CurrentSprint,
    int CurrentProgressPercent,
    BoardStatus CurrentStatus)
{
    public static JiraSuggestionDto Unavailable(string reason) => new(
        false, reason, null, 0, 0, 0, 0,
        BoardStatus.OnTrack, string.Empty, string.Empty, string.Empty,
        null, 0, BoardStatus.OnTrack);
}

public sealed class GetJiraSuggestionQueryHandler(IAppDbContext db, IJiraClient jira)
    : IRequestHandler<GetJiraSuggestionQuery, JiraSuggestionDto>
{
    public async Task<JiraSuggestionDto> Handle(
        GetJiraSuggestionQuery request, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.BoardId} was not found.");

        if (!await jira.IsEnabledAsync(cancellationToken))
        {
            return JiraSuggestionDto.Unavailable(
                "Jira is not configured for this deployment.");
        }

        if (string.IsNullOrWhiteSpace(board.JiraProjectKey))
        {
            return JiraSuggestionDto.Unavailable(
                "This board has no Jira project key. Add one to enable sync.");
        }

        var snapshot = await jira.GetSnapshotAsync(
            board.JiraProjectKey, board.JiraBoardId, cancellationToken);

        if (snapshot is null)
        {
            return JiraSuggestionDto.Unavailable(
                $"Jira returned nothing for project '{board.JiraProjectKey}'.");
        }

        return new JiraSuggestionDto(
            true,
            null,
            snapshot.SprintName,
            snapshot.DoneIssues,
            snapshot.TotalIssues,
            snapshot.BlockedIssues,
            snapshot.SuggestedProgressPercent,
            snapshot.SuggestedStatus,
            BoardStatusMetadata.Label(snapshot.SuggestedStatus),
            BoardStatusMetadata.Color(snapshot.SuggestedStatus),
            snapshot.Rationale,
            board.Sprint,
            board.ProgressPercent,
            board.Status);
    }
}
