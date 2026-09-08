using System.Net.Http.Headers;
using System.Text.Json;
using Application.Abstractions;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Read-only Smartsheet client.
///
/// Credentials come from <see cref="ISmartsheetSettingsService"/> on every call rather
/// than being captured at construction, so saving the settings screen takes effect
/// immediately — no restart, and no stale token cached in a singleton.
///
/// It never writes to Smartsheet, and the caller does not write the result straight to a
/// board unless auto-apply is explicitly switched on.
/// </summary>
public sealed class SmartsheetClient(
    HttpClient http,
    ISmartsheetSettingsService settings,
    ILogger<SmartsheetClient> logger) : ISmartsheetClient
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        await settings.GetCredentialsAsync(cancellationToken) is not null;

    public async Task<SmartsheetSnapshot?> GetSnapshotAsync(string sheetId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);
        if (credentials is null) return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{credentials.BaseUrl}/sheets/{Uri.EscapeDataString(sheetId)}");

            // Smartsheet uses a bearer token, unlike Jira's Basic email+token. Built per
            // request so a credential change takes effect without recycling the client.
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Smartsheet sheet {SheetId} returned {Status}",
                    sheetId, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Summarise(document.RootElement, credentials);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A Smartsheet outage must not break the board; the caller reports it as
            // unavailable.
            logger.LogWarning(ex, "Could not reach Smartsheet for sheet {SheetId}", sheetId);
            return null;
        }
    }

    /// <summary>
    /// Turns a sheet into a progress and status suggestion.
    ///
    /// A sheet has no fixed shape, so this reads the two columns the admin nominated. The
    /// percentage prefers a real "% Complete" column and only falls back to counting
    /// finished rows — the snapshot records which, because those are different levels of
    /// confidence and the PO should see which one they are being shown.
    /// </summary>
    private static SmartsheetSnapshot Summarise(JsonElement root, SmartsheetCredentials credentials)
    {
        var sheetName = root.TryGetProperty("name", out var name)
            ? name.GetString() ?? "Sheet"
            : "Sheet";

        var progressColumnId = FindColumnId(root, credentials.ProgressColumn);
        var statusColumnId = FindColumnId(root, credentials.StatusColumn);

        var total = 0;
        var done = 0;
        var blocked = 0;
        var progressSum = 0d;
        var progressCount = 0;

        if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                total++;

                if (!row.TryGetProperty("cells", out var cells)
                    || cells.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var cell in cells.EnumerateArray())
                {
                    if (!cell.TryGetProperty("columnId", out var columnId)) continue;
                    var id = columnId.GetInt64();

                    if (progressColumnId is { } pid && id == pid && ReadNumber(cell) is { } value)
                    {
                        // Smartsheet stores "% Complete" as a fraction (0.75), but people
                        // also type whole numbers. Accept both rather than reporting 0%.
                        progressSum += value <= 1 ? value * 100 : value;
                        progressCount++;
                    }

                    if (statusColumnId is { } sid && id == sid)
                    {
                        var text = ReadText(cell);
                        if (text is null) continue;

                        if (text.Contains("block", StringComparison.OrdinalIgnoreCase))
                        {
                            blocked++;
                        }
                        else if (IsDone(text))
                        {
                            done++;
                        }
                    }
                }
            }
        }

        var fromColumn = progressCount > 0;

        var progress = fromColumn
            ? (int)Math.Round(Math.Clamp(progressSum / progressCount, 0, 100))
            : total == 0 ? 0 : (int)Math.Round(done * 100d / total);

        // Conservative on purpose, and matching the Jira rules so the two integrations do
        // not disagree about what "At Risk" means.
        var (suggested, why) = (blocked, total) switch
        {
            ( > 0, > 0) when blocked * 100d / total >= 20 =>
                (BoardStatus.Blocked, $"{blocked} of {total} rows are blocked."),
            ( > 0, _) =>
                (BoardStatus.AtRisk, $"{blocked} blocked row(s) out of {total}."),
            (_, 0) =>
                (BoardStatus.OnTrack, "The sheet has no rows."),
            _ when progress >= 100 =>
                (BoardStatus.Delivered, $"All {total} rows are complete."),
            _ =>
                (BoardStatus.OnTrack, $"{done} of {total} rows done, none blocked.")
        };

        var source = fromColumn
            ? $"Progress averaged from \"{credentials.ProgressColumn}\"."
            : statusColumnId is null
                ? $"Neither \"{credentials.ProgressColumn}\" nor \"{credentials.StatusColumn}\" " +
                  "was found on this sheet, so progress could not be read."
                : $"No \"{credentials.ProgressColumn}\" column, so progress was counted from " +
                  $"\"{credentials.StatusColumn}\".";

        return new SmartsheetSnapshot(
            sheetName, done, total, blocked, progress, fromColumn, suggested, $"{why} {source}");
    }

    /// <summary>Matches a column by its title, case-insensitively.</summary>
    private static long? FindColumnId(JsonElement root, string title)
    {
        if (!root.TryGetProperty("columns", out var columns)
            || columns.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var column in columns.EnumerateArray())
        {
            if (column.TryGetProperty("title", out var columnTitle)
                && string.Equals(columnTitle.GetString(), title, StringComparison.OrdinalIgnoreCase)
                && column.TryGetProperty("id", out var id))
            {
                return id.GetInt64();
            }
        }

        return null;
    }

    private static double? ReadNumber(JsonElement cell)
    {
        if (!cell.TryGetProperty("value", out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            // A percent cell can arrive as text when the column is free-form.
            JsonValueKind.String when double.TryParse(
                value.GetString()?.TrimEnd('%'), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ReadText(JsonElement cell)
    {
        if (cell.TryGetProperty("displayValue", out var display)
            && display.ValueKind == JsonValueKind.String)
        {
            return display.GetString();
        }

        return cell.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>The words Smartsheet templates actually use for a finished row.</summary>
    private static bool IsDone(string status) =>
        status.Contains("complete", StringComparison.OrdinalIgnoreCase)
        || status.Contains("done", StringComparison.OrdinalIgnoreCase)
        || status.Contains("closed", StringComparison.OrdinalIgnoreCase);
}
