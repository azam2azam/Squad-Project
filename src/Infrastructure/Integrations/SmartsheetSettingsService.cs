using Application.Abstractions;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Stores the Smartsheet connection in the database, with the access token encrypted
/// through ASP.NET Core Data Protection.
///
/// Mirrors <see cref="JiraSettingsService"/> deliberately, including the rule that
/// configuration wins: if Smartsheet__AccessToken is set in the environment, that is what
/// the client uses and the settings screen says so. A locked-down environment can pin the
/// credentials outside the reach of an application admin.
/// </summary>
public sealed class SmartsheetSettingsService(
    IAppDbContext db,
    IDataProtectionProvider protectionProvider,
    IConfiguration configuration,
    ICurrentUser currentUser,
    ILogger<SmartsheetSettingsService> logger) : ISmartsheetSettingsService
{
    // A named purpose scopes the key: ciphertext from here cannot be decrypted by another
    // part of the app that happens to use Data Protection — including the Jira token.
    private readonly IDataProtector _protector =
        protectionProvider.CreateProtector("SquadStatusBoard.SmartsheetAccessToken.v1");

    public async Task<SmartsheetSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        var fromConfig = ConfiguredCredentials();

        if (settings is null)
        {
            return new SmartsheetSettingsView(
                Configured: fromConfig is not null,
                Enabled: fromConfig is not null,
                BaseUrl: fromConfig?.BaseUrl ?? SmartsheetSettings.DefaultBaseUrl,
                TokenHint: fromConfig is null ? null : Mask(fromConfig.AccessToken),
                AutoApply: false,
                SyncIntervalMinutes: 0,
                ProgressColumn: fromConfig?.ProgressColumn ?? SmartsheetSettings.DefaultProgressColumn,
                StatusColumn: fromConfig?.StatusColumn ?? SmartsheetSettings.DefaultStatusColumn,
                UpdatedBy: null,
                UpdatedAt: null,
                LastSyncAt: null,
                LastSyncResult: null,
                OverriddenByConfiguration: fromConfig is not null);
        }

        return new SmartsheetSettingsView(
            Configured: settings.IsUsable || fromConfig is not null,
            Enabled: settings.Enabled || fromConfig is not null,
            BaseUrl: fromConfig?.BaseUrl ?? settings.BaseUrl,
            TokenHint: fromConfig is not null ? Mask(fromConfig.AccessToken) : NullIfBlank(settings.TokenHint),
            AutoApply: settings.AutoApply,
            SyncIntervalMinutes: settings.SyncIntervalMinutes,
            ProgressColumn: settings.ProgressColumn,
            StatusColumn: settings.StatusColumn,
            UpdatedBy: settings.UpdatedBy,
            UpdatedAt: settings.UpdatedAt,
            LastSyncAt: settings.LastSyncAt,
            LastSyncResult: settings.LastSyncResult,
            OverriddenByConfiguration: fromConfig is not null);
    }

    public async Task<SmartsheetSettingsView> SaveAsync(SaveSmartsheetSettings request,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        var (cipher, hint) = string.IsNullOrWhiteSpace(request.AccessToken)
            ? (string.Empty, string.Empty)
            : (_protector.Protect(request.AccessToken.Trim()), Mask(request.AccessToken.Trim()));

        if (settings is null)
        {
            settings = new SmartsheetSettings(request.BaseUrl, cipher, hint,
                request.Enabled, currentUser.DisplayName);
            db.SmartsheetSettings.Add(settings);
        }
        else
        {
            settings.Update(request.BaseUrl, cipher, hint, request.Enabled, currentUser.DisplayName);
        }

        settings.ConfigureSync(request.AutoApply, request.SyncIntervalMinutes);
        settings.ConfigureColumns(request.ProgressColumn, request.StatusColumn);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Smartsheet connection updated by {User} (enabled={Enabled}, autoApply={AutoApply}, " +
            "interval={Interval}m).",
            currentUser.DisplayName, settings.Enabled, settings.AutoApply,
            settings.SyncIntervalMinutes);

        return await GetAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        db.SmartsheetSettings.Remove(settings);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Smartsheet connection removed by {User}.", currentUser.DisplayName);
    }

    public async Task<SmartsheetCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        // Configuration wins, so a pinned deployment cannot be redirected from the UI.
        var fromConfig = ConfiguredCredentials();
        if (fromConfig is not null) return fromConfig;

        var settings = await LoadAsync(cancellationToken);
        if (settings is null || !settings.IsUsable) return null;

        try
        {
            return new SmartsheetCredentials(
                settings.BaseUrl,
                _protector.Unprotect(settings.EncryptedAccessToken),
                settings.ProgressColumn,
                settings.StatusColumn);
        }
        catch (Exception ex)
        {
            // Happens when the Data Protection key ring is lost — for instance a container
            // without a persisted key directory. Say so plainly: the fix is to re-enter the
            // token, not to debug a decryption error.
            logger.LogError(ex,
                "The stored Smartsheet token could not be decrypted. The Data Protection keys " +
                "have probably changed; re-enter the token in Settings.");
            return null;
        }
    }

    public async Task RecordSyncAsync(string result, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        settings.RecordSync(result);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<SmartsheetSettings?> LoadAsync(CancellationToken cancellationToken) =>
        db.SmartsheetSettings.FirstOrDefaultAsync(
            s => s.Id == SmartsheetSettings.SingletonId, cancellationToken);

    /// <summary>Credentials from environment configuration, when a deployment pins them.</summary>
    private SmartsheetCredentials? ConfiguredCredentials()
    {
        var section = configuration.GetSection("Smartsheet");

        var baseUrl = section["BaseUrl"]?.TrimEnd('/');
        var token = section["AccessToken"];

        return section.GetValue("Enabled", false) && !string.IsNullOrWhiteSpace(token)
            ? new SmartsheetCredentials(
                string.IsNullOrWhiteSpace(baseUrl) ? SmartsheetSettings.DefaultBaseUrl : baseUrl,
                token,
                Or(section["ProgressColumn"], SmartsheetSettings.DefaultProgressColumn),
                Or(section["StatusColumn"], SmartsheetSettings.DefaultStatusColumn))
            : null;
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>Shows only the last four characters — enough to identify, not to use.</summary>
    private static string Mask(string token) =>
        token.Length <= 4
            ? new string('•', 8)
            : new string('•', 8) + token[^4..];

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
