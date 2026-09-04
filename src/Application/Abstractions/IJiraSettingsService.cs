namespace Application.Abstractions;

/// <summary>
/// Reads and writes the Jira connection, handling encryption of the API token.
///
/// The plaintext token exists in exactly two places: the moment an admin submits it, and
/// the moment the HTTP client builds an auth header. It is never returned to a client,
/// never logged, and never stored in the clear.
/// </summary>
public interface IJiraSettingsService
{
    /// <summary>The connection as an admin should see it — token masked, never decrypted.</summary>
    Task<JiraSettingsView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the connection. A blank <paramref name="apiToken"/> keeps the stored one,
    /// so re-saving the form without retyping the secret does not wipe it.
    /// </summary>
    Task<JiraSettingsView> SaveAsync(SaveJiraSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the stored connection entirely.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The credentials the HTTP client needs. Returns null when Jira is not usable, so
    /// callers cannot accidentally build a half-configured request.
    /// </summary>
    Task<JiraCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);

    Task RecordSyncAsync(string result, CancellationToken cancellationToken = default);
}

/// <summary>What an admin sees. Deliberately has no field that could carry the token.</summary>
public sealed record JiraSettingsView(
    bool Configured,
    bool Enabled,
    string BaseUrl,
    string Email,
    /// <summary>e.g. "••••••••3f9a" — enough to identify the token, not to use it.</summary>
    string? TokenHint,
    bool AutoApply,
    int SyncIntervalMinutes,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastSyncAt,
    string? LastSyncResult,
    /// <summary>True when environment configuration is supplying the connection instead.</summary>
    bool OverriddenByConfiguration);

public sealed record SaveJiraSettings(
    string BaseUrl,
    string Email,
    string? ApiToken,
    bool Enabled,
    bool AutoApply,
    int SyncIntervalMinutes);

/// <summary>Decrypted credentials, used only to build a request.</summary>
public sealed record JiraCredentials(string BaseUrl, string Email, string ApiToken);
