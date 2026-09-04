using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// The connection to the company's Jira, managed from the app rather than only from
/// deployment configuration.
///
/// A single row: there is one Jira per deployment. <see cref="SingletonId"/> is fixed so
/// the row can be upserted without a "which one?" question ever arising.
///
/// The API token is held **encrypted** — see the settings service. This entity never
/// sees the plaintext and never hands it back out; the UI shows a masked hint so an
/// admin can tell a token is present without being able to read it.
/// </summary>
public class JiraSettings : Entity
{
    /// <summary>There is one Jira connection per deployment, so the row has a fixed id.</summary>
    public static readonly Guid SingletonId = new("cf1a7e90-0000-4000-b000-00000000000d");

    private JiraSettings() { }

    public JiraSettings(string baseUrl, string email, string encryptedApiToken,
        string tokenHint, bool enabled, string updatedBy)
    {
        Id = SingletonId;
        Update(baseUrl, email, encryptedApiToken, tokenHint, enabled, updatedBy);
    }

    /// <summary>e.g. https://yourcompany.atlassian.net — no trailing slash.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The Atlassian account the API token belongs to.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Ciphertext only. Never logged, never returned to a client.</summary>
    public string EncryptedApiToken { get; private set; } = string.Empty;

    /// <summary>
    /// Last four characters of the token, so an admin can confirm *which* token is
    /// stored without it being readable.
    /// </summary>
    public string TokenHint { get; private set; } = string.Empty;

    /// <summary>Off by default: configuring a connection is not the same as turning it on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// When true, a successful sync writes the pulled progress and status straight to the
    /// board. Off by default — the spec's rule is that Jira suggests and a human accepts,
    /// and silently rewriting somebody's status is the kind of thing that erodes trust in
    /// the numbers.
    /// </summary>
    public bool AutoApply { get; private set; }

    /// <summary>How often the background sync runs. Zero disables it.</summary>
    public int SyncIntervalMinutes { get; private set; }

    public string UpdatedBy { get; private set; } = "system";
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastSyncAt { get; private set; }
    public string? LastSyncResult { get; private set; }

    public void Update(string baseUrl, string email, string encryptedApiToken,
        string tokenHint, bool enabled, string updatedBy)
    {
        SetBaseUrl(baseUrl);
        SetEmail(email);

        // An empty token means "leave the stored one alone" — the UI cannot send back a
        // value it was never given, so a blank field must not wipe a working connection.
        if (!string.IsNullOrWhiteSpace(encryptedApiToken))
        {
            EncryptedApiToken = encryptedApiToken;
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
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordSync(string result)
    {
        LastSyncAt = DateTimeOffset.UtcNow;
        LastSyncResult = result.Length > 500 ? result[..500] : result;
    }

    /// <summary>True once there is enough to actually call Jira.</summary>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(EncryptedApiToken);

    private void SetBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            BaseUrl = string.Empty;
            return;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DomainException(
                "The Jira URL must be a full address, for example https://yourcompany.atlassian.net");
        }

        // Credentials would otherwise travel in clear text to an internal host.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new DomainException(
                "Use https for the Jira URL. An API token sent over http can be read in transit.");
        }

        BaseUrl = trimmed;
    }

    private void SetEmail(string email)
    {
        var trimmed = email?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.Contains('@'))
        {
            throw new DomainException("The Jira account must be an email address.");
        }

        Email = trimmed;
    }
}
