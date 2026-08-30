using Application.Abstractions;

namespace Infrastructure.Integrations;

/// <summary>
/// Null-object Jira client used when Jira is not configured. Reports itself disabled
/// so the UI hides the sync affordance rather than offering a button that fails.
/// Replaced by the HTTP client in M5.
/// </summary>
public sealed class DisabledJiraClient : IJiraClient
{
    public bool IsEnabled => false;

    public Task<JiraSnapshot?> GetSnapshotAsync(string projectKey, string? boardId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<JiraSnapshot?>(null);
}
