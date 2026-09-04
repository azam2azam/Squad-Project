using Domain.Enums;

namespace Application.Abstractions;

/// <summary>
/// Read-only Jira access. Config-gated: when Jira is not configured the implementation
/// reports Enabled = false and the UI hides the sync affordance.
/// </summary>
public interface IJiraClient
{
    /// <summary>
    /// Whether a usable connection exists. Async because the credentials live in the
    /// database now — a synchronous property here would mean sync-over-async on every
    /// capabilities check.
    /// </summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the active sprint and issue counts for a board. Never writes to the board —
    /// the caller shows the suggestion and lets the Product Owner accept it.
    /// </summary>
    Task<JiraSnapshot?> GetSnapshotAsync(string projectKey, string? boardId,
        CancellationToken cancellationToken = default);
}

/// <summary>A suggestion derived from Jira, presented for acceptance rather than applied.</summary>
public sealed record JiraSnapshot(
    string? SprintName,
    int DoneIssues,
    int TotalIssues,
    int BlockedIssues,
    int SuggestedProgressPercent,
    BoardStatus SuggestedStatus,
    string Rationale);
