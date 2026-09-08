namespace Application.Abstractions;

/// <summary>
/// Reads and writes the Smartsheet connection, handling encryption of the access token.
///
/// The plaintext token exists in exactly two places: the moment an admin submits it, and
/// the moment the HTTP client builds an auth header. It is never returned to a client,
/// never logged, and never stored in the clear.
/// </summary>
public interface ISmartsheetSettingsService
{
    /// <summary>The connection as an admin should see it — token masked, never decrypted.</summary>
    Task<SmartsheetSettingsView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the connection. A blank <paramref name="settings"/> token keeps the stored
    /// one, so re-saving the form without retyping the secret does not wipe it.
    /// </summary>
    Task<SmartsheetSettingsView> SaveAsync(SaveSmartsheetSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the stored connection entirely.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The credentials the HTTP client needs. Returns null when Smartsheet is not usable,
    /// so callers cannot accidentally build a half-configured request.
    /// </summary>
    Task<SmartsheetCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);

    Task RecordSyncAsync(string result, CancellationToken cancellationToken = default);
}

/// <summary>What an admin sees. Deliberately has no field that could carry the token.</summary>
public sealed record SmartsheetSettingsView(
    bool Configured,
    bool Enabled,
    string BaseUrl,
    /// <summary>e.g. "••••••••3f9a" — enough to identify the token, not to use it.</summary>
    string? TokenHint,
    bool AutoApply,
    int SyncIntervalMinutes,
    string ProgressColumn,
    string StatusColumn,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastSyncAt,
    string? LastSyncResult,
    /// <summary>True when environment configuration is supplying the connection instead.</summary>
    bool OverriddenByConfiguration);

public sealed record SaveSmartsheetSettings(
    string BaseUrl,
    string? AccessToken,
    bool Enabled,
    bool AutoApply,
    int SyncIntervalMinutes,
    string? ProgressColumn,
    string? StatusColumn);

/// <summary>
/// Decrypted credentials plus the column mapping, used only to build and read a request.
/// The column titles travel with the credentials because a sheet has no fixed shape —
/// without them the client would not know which cell means progress.
/// </summary>
public sealed record SmartsheetCredentials(
    string BaseUrl,
    string AccessToken,
    string ProgressColumn,
    string StatusColumn);
