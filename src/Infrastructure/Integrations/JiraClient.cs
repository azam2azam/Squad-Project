using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Read-only Jira Cloud client. Config-gated: when Jira is not configured this reports
/// itself disabled and the UI hides the sync affordance rather than offering a button
/// that fails.
///
/// It never writes to Jira, and the caller never writes the result straight to a board —
/// the Product Owner reviews the suggestion first (spec section 10).
/// </summary>
public sealed class JiraClient : IJiraClient
{
    private readonly HttpClient _http;
    private readonly ILogger<JiraClient> _logger;
    private readonly string? _baseUrl;

    public JiraClient(HttpClient http, IConfiguration configuration, ILogger<JiraClient> logger)
    {
        _http = http;
        _logger = logger;

        var section = configuration.GetSection("Jira");
        var enabled = section.GetValue("Enabled", false);
        _baseUrl = section["BaseUrl"]?.TrimEnd('/');
        var email = section["Email"];
        var apiToken = section["ApiToken"];

        IsEnabled = enabled
            && !string.IsNullOrWhiteSpace(_baseUrl)
            && !string.IsNullOrWhiteSpace(email)
            && !string.IsNullOrWhiteSpace(apiToken);

        if (IsEnabled)
        {
            // Jira Cloud uses Basic auth with email + API token.
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _http.Timeout = TimeSpan.FromSeconds(20);
        }
    }

    public bool IsEnabled { get; }

    public async Task<JiraSnapshot?> GetSnapshotAsync(string projectKey, string? boardId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            var jql = Uri.EscapeDataString($"project = \"{projectKey}\" ORDER BY updated DESC");
            var url = $"{_baseUrl}/rest/api/3/search?jql={jql}&maxResults=200" +
                      "&fields=status,statuscategorychangedate,sprint,customfield_10020";

            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jira search for {ProjectKey} returned {Status}",
                    projectKey, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Summarise(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A Jira outage must not break the board; the caller reports it as unavailable.
            _logger.LogWarning(ex, "Could not reach Jira for project {ProjectKey}", projectKey);
            return null;
        }
    }

    /// <summary>
    /// Turns raw issues into a progress and status suggestion. Kept deliberately simple
    /// and explainable — the Rationale is shown to the PO so they can judge it.
    /// </summary>
    private static JiraSnapshot Summarise(JsonElement root)
    {
        var total = 0;
        var done = 0;
        var blocked = 0;
        string? sprintName = null;

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

        var progress = total == 0 ? 0 : (int)Math.Round(done * 100d / total);

        // Thresholds chosen to be conservative: a suggestion that over-reports health is
        // worse than one a PO has to correct upward.
        var (suggested, rationale) = (blocked, total) switch
        {
            ( > 0, > 0) when blocked * 100d / total >= 20 =>
                (BoardStatus.Blocked,
                    $"{blocked} of {total} issues are blocked."),
            ( > 0, _) =>
                (BoardStatus.AtRisk,
                    $"{blocked} blocked issue(s) out of {total}."),
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
