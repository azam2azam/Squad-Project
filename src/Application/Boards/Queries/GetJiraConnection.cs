using Application.Abstractions;
using MediatR;

namespace Application.Boards.Queries;

/// <summary>
/// Whether Jira is wired up, and whether the credentials actually work.
///
/// Separate from <c>/metadata/capabilities</c>, which only says whether the feature is
/// switched on. This one makes a real call, so an admin can tell "not configured" from
/// "configured but the token is wrong" without guessing.
/// </summary>
public sealed record GetJiraConnectionQuery(string? ProbeProjectKey = null)
    : IRequest<JiraConnectionDto>;

public sealed record JiraConnectionDto(
    bool Enabled,
    bool Reachable,
    string Message,
    string? ProbedProjectKey,
    int? IssuesSeen);

public sealed class GetJiraConnectionQueryHandler(IJiraClient jira, IBoardAuthorizer authorizer)
    : IRequestHandler<GetJiraConnectionQuery, JiraConnectionDto>
{
    public async Task<JiraConnectionDto> Handle(
        GetJiraConnectionQuery request, CancellationToken cancellationToken)
    {
        // Probing an external system with our credentials is an administrative action.
        authorizer.EnsureIsAdmin();

        if (!jira.IsEnabled)
        {
            return new JiraConnectionDto(
                false, false,
                "Jira is not configured. Set Jira__Enabled, Jira__BaseUrl, Jira__Email and " +
                "Jira__ApiToken, then restart the API.",
                null, null);
        }

        if (string.IsNullOrWhiteSpace(request.ProbeProjectKey))
        {
            return new JiraConnectionDto(
                true, false,
                "Jira is configured. Supply a project key to test the credentials against it.",
                null, null);
        }

        var snapshot = await jira.GetSnapshotAsync(
            request.ProbeProjectKey, null, cancellationToken);

        return snapshot is null
            ? new JiraConnectionDto(
                true, false,
                $"Jira is configured but returned nothing for project " +
                $"'{request.ProbeProjectKey}'. Check the project key, the account's " +
                "permissions, and that the API token is still valid.",
                request.ProbeProjectKey, null)
            : new JiraConnectionDto(
                true, true,
                $"Connected. Read {snapshot.TotalIssues} issue(s) from " +
                $"'{request.ProbeProjectKey}'.",
                request.ProbeProjectKey, snapshot.TotalIssues);
    }
}
