using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Read-only Jira Cloud client.
///
/// Credentials come from <see cref="IJiraSettingsService"/> on every call rather than
/// being captured at construction, so saving the settings screen takes effect
/// immediately — no restart, and no stale token cached in a singleton.
///
/// It never writes to Jira, and the caller does not write the result straight to a board
/// unless auto-apply is explicitly switched on (spec section 10).
/// </summary>
public sealed class JiraClient(
    HttpClient http,
    IJiraSettingsService settings,
    ILogger<JiraClient> logger) : IJiraClient
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        await settings.GetCredentialsAsync(cancellationToken) is not null;

    public async Task<JiraSnapshot?> GetSnapshotAsync(string projectKey, string? boardId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);
        if (credentials is null)
        {
            return null;
        }

        try
        {
            var tally = new Tally();
            string? pageToken = null;

            // The replacement endpoint pages with a token and does not report a total, so
            // the issues have to be walked. Capped rather than unbounded: a project with
            // more than a thousand issues does not produce a more useful percentage, and an
            // integration that can loop forever on somebody else's data eventually will.
            for (var page = 0; page < 10; page++)
            {
                using var request = BuildSearchRequest(credentials, projectKey, pageToken);
                using var response = await http.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Jira search for {ProjectKey} returned {Status}",
                        projectKey, (int)response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream,
                    cancellationToken: cancellationToken);

                Accumulate(document.RootElement, tally);

                pageToken = NextPageToken(document.RootElement);
                if (pageToken is null) break;
            }

            return Summarise(tally);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A Jira outage must not break the board; the caller reports it as unavailable.
            logger.LogWarning(ex, "Could not reach Jira for project {ProjectKey}", projectKey);
            return null;
        }
    }

    /// <summary>
    /// Builds one page of the search.
    ///
    /// The endpoint is <c>/rest/api/3/search/jql</c>, not <c>/rest/api/3/search</c>:
    /// Atlassian removed the latter from Jira Cloud, and it now answers <b>410 Gone</b> to
    /// every request — which looks exactly like "the project returned nothing" from the
    /// outside, whatever key you type.
    ///
    /// The difference that matters when reading this: the new endpoint returns no total and
    /// pages with an opaque token rather than a start index.
    /// </summary>
    private static HttpRequestMessage BuildSearchRequest(JiraCredentials credentials,
        string projectKey, string? pageToken)
    {
        var jql = Uri.EscapeDataString($"project = \"{projectKey}\" ORDER BY updated DESC");
        var url = $"{credentials.BaseUrl}/rest/api/3/search/jql?jql={jql}&maxResults=100" +
                  "&fields=status,statuscategorychangedate,sprint,customfield_10020";

        if (pageToken is not null)
        {
            url += $"&nextPageToken={Uri.EscapeDataString(pageToken)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Jira Cloud uses Basic auth with email + API token. Built per request so a
        // credential change takes effect without recycling the client.
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credentials.Email}:{credentials.ApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>
    /// Running counts across pages. Held as numbers rather than a list of issues because a
    /// <see cref="JsonElement"/> does not outlive the document it came from, and the
    /// document is disposed at the end of each page.
    /// </summary>
    private sealed class Tally
    {
        public int Total;
        public int Done;
        public int Blocked;
        public string? SprintName;
    }

    /// <summary>The token for the next page, or null when this was the last one.</summary>
    private static string? NextPageToken(JsonElement root)
    {
        if (root.TryGetProperty("isLast", out var isLast) && isLast.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return root.TryGetProperty("nextPageToken", out var token)
               && token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : null;
    }

    private static void Accumulate(JsonElement root, Tally tally)
    {
        var total = tally.Total;
        var done = tally.Done;
        var blocked = tally.Blocked;
        var sprintName = tally.SprintName;

        if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in issues.EnumerateArray())
            {
                total++;

                if (!issue.TryGetProperty("fields", out var fields)) continue;

                var categoryKey = fields
                    .TryGetProperty("status", out var status) && status
                    .TryGetProperty("statusCategory", out var category) && category
                    .TryGetProperty("key", out var key)
                    ? key.GetString()
                    : null;

                if (string.Equals(categoryKey, "done", StringComparison.OrdinalIgnoreCase))
                {
                    done++;
                }

                var statusName = status.ValueKind == JsonValueKind.Object
                                 && status.TryGetProperty("name", out var name)
                    ? name.GetString()
                    : null;

                if (statusName is not null
                    && statusName.Contains("block", StringComparison.OrdinalIgnoreCase))
                {
                    blocked++;
                }

                sprintName ??= ReadSprintName(fields);
            }
        }

        tally.Total = total;
        tally.Done = done;
        tally.Blocked = blocked;
        tally.SprintName = sprintName;
    }

    /// <summary>
    /// Turns the counts into a progress and status suggestion. Kept deliberately simple
    /// and explainable — the rationale is shown to the PO so they can judge it.
    /// </summary>
    private static JiraSnapshot Summarise(Tally tally)
    {
        var total = tally.Total;
        var done = tally.Done;
        var blocked = tally.Blocked;
        var sprintName = tally.SprintName;

        var progress = total == 0 ? 0 : (int)Math.Round(done * 100d / total);

        // Conservative on purpose: a suggestion that over-reports health is worse than
        // one a PO has to correct upward.
        var (suggested, rationale) = (blocked, total) switch
        {
            ( > 0, > 0) when blocked * 100d / total >= 20 =>
                (BoardStatus.Blocked, $"{blocked} of {total} issues are blocked."),
            ( > 0, _) =>
                (BoardStatus.AtRisk, $"{blocked} blocked issue(s) out of {total}."),
            (_, 0) =>
                (BoardStatus.OnTrack, "No issues found in this project."),
            _ when progress >= 100 =>
                (BoardStatus.Delivered, $"All {total} issues are done."),
            _ =>
                (BoardStatus.OnTrack, $"{done} of {total} issues done, none blocked.")
        };

        return new JiraSnapshot(sprintName, done, total, blocked, progress, suggested, rationale);
    }

    /// <summary>
    /// Sprint lives in a customfield whose id differs per Jira site; 10020 is the common
    /// default. Returns null rather than guessing when the shape is unfamiliar.
    /// </summary>
    private static string? ReadSprintName(JsonElement fields)
    {
        if (!fields.TryGetProperty("customfield_10020", out var sprints)
            || sprints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var sprint in sprints.EnumerateArray())
        {
            if (sprint.ValueKind == JsonValueKind.Object
                && sprint.TryGetProperty("state", out var state)
                && string.Equals(state.GetString(), "active", StringComparison.OrdinalIgnoreCase)
                && sprint.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }

        return null;
    }
}
