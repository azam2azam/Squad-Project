using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// The connection to the company's Smartsheet, managed from the app rather than only from
/// deployment configuration.
///
/// A sibling of <see cref="JiraSettings"/> rather than a shared table, because the two
/// providers genuinely differ: Smartsheet authenticates with a bearer token and no
/// account email, and it needs to be told which columns carry progress and status —
/// a sheet is a spreadsheet, so unlike a Jira issue there is no universal shape to read.
///
/// The access token is held **encrypted** — see the settings service. This entity never
/// sees the plaintext and never hands it back out.
/// </summary>
public class SmartsheetSettings : Entity
{
    /// <summary>One Smartsheet connection per deployment, so the row has a fixed id.</summary>
    public static readonly Guid SingletonId = new("cf1a7e90-0000-4000-b000-00000000000e");

    /// <summary>Smartsheet's public API. Only overridden for a regional or test endpoint.</summary>
    public const string DefaultBaseUrl = "https://api.smartsheet.com/2.0";

    /// <summary>The column titles most Smartsheet project templates already use.</summary>
    public const string DefaultProgressColumn = "% Complete";
    public const string DefaultStatusColumn = "Status";

    private SmartsheetSettings() { }

    public SmartsheetSettings(string baseUrl, string encryptedAccessToken, string tokenHint,
        bool enabled, string updatedBy)
    {
        Id = SingletonId;
        Update(baseUrl, encryptedAccessToken, tokenHint, enabled, updatedBy);
        ProgressColumn = DefaultProgressColumn;
        StatusColumn = DefaultStatusColumn;
    }

    /// <summary>e.g. https://api.smartsheet.com/2.0 — no trailing slash.</summary>
    public string BaseUrl { get; private set; } = DefaultBaseUrl;

    /// <summary>Ciphertext only. Never logged, never returned to a client.</summary>
    public string EncryptedAccessToken { get; private set; } = string.Empty;

    /// <summary>
    /// Last four characters of the token, so an admin can confirm *which* token is stored
    /// without it being readable.
    /// </summary>
    public string TokenHint { get; private set; } = string.Empty;

    /// <summary>Off by default: configuring a connection is not the same as turning it on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// When true a successful sync writes straight to the board. Off by default, for the
    /// same reason as Jira: silently rewriting somebody's status erodes trust in the
    /// numbers faster than a stale board does.
    /// </summary>
    public bool AutoApply { get; private set; }

    /// <summary>How often the background sync runs. Zero disables it.</summary>
    public int SyncIntervalMinutes { get; private set; }

    /// <summary>
    /// Title of the column holding a per-row completion percentage. A sheet has no fixed
    /// shape, so the mapping has to be stated rather than guessed.
    /// </summary>
    public string ProgressColumn { get; private set; } = DefaultProgressColumn;

    /// <summary>Title of the column holding a per-row status.</summary>
    public string StatusColumn { get; private set; } = DefaultStatusColumn;

    public string UpdatedBy { get; private set; } = "system";
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSyncAt { get; private set; }
    public string? LastSyncResult { get; private set; }

    /// <summary>Usable means: switched on, and carrying everything a request needs.</summary>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(EncryptedAccessToken);

    public void Update(string baseUrl, string encryptedAccessToken, string tokenHint,
        bool enabled, string updatedBy)
    {
        SetBaseUrl(baseUrl);

        // An empty token means "leave the stored one alone" — the UI cannot send back a
        // value it was never given, so a blank field must not wipe a working connection.
        if (!string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            EncryptedAccessToken = encryptedAccessToken;
            TokenHint = tokenHint;
        }

        Enabled = enabled;
        UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ConfigureSync(bool autoApply, int intervalMinutes)
    {
        if (intervalMinutes is < 0 or > 1440)
        {
            throw new DomainException(
                "Sync interval must be between 0 and 1440 minutes (0 disables it).");
        }

        AutoApply = autoApply;
        SyncIntervalMinutes = intervalMinutes;
    }

    /// <summary>Blank falls back to the conventional titles rather than reading nothing.</summary>
    public void ConfigureColumns(string? progressColumn, string? statusColumn)
    {
        ProgressColumn = string.IsNullOrWhiteSpace(progressColumn)
            ? DefaultProgressColumn
            : progressColumn.Trim();

        StatusColumn = string.IsNullOrWhiteSpace(statusColumn)
            ? DefaultStatusColumn
            : statusColumn.Trim();
    }

    public void RecordSync(string result)
    {
        LastSyncAt = DateTimeOffset.UtcNow;
        LastSyncResult = result;
    }

    private void SetBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            BaseUrl = DefaultBaseUrl;
            return;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DomainException(
                "The Smartsheet API URL must be a full address, for example " +
                "https://api.smartsheet.com/2.0");
        }

        // A bearer token would otherwise travel in clear text to an internal host.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new DomainException(
                "Use https for the Smartsheet API URL. An access token sent over http can " +
                "be read in transit.");
        }

        BaseUrl = trimmed;
    }
}
