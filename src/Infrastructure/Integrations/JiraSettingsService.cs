using Application.Abstractions;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Stores the Jira connection in the database, with the API token encrypted through
/// ASP.NET Core Data Protection.
///
/// Two sources are supported, and configuration wins: if Jira__ApiToken is set in the
/// environment, that is what the client uses and the settings screen says so. That keeps
/// an existing env-var deployment working, and means a locked-down environment can pin
/// the credentials outside the reach of an application admin.
/// </summary>
public sealed class JiraSettingsService(
    IAppDbContext db,
    IDataProtectionProvider protectionProvider,
    IConfiguration configuration,
    ICurrentUser currentUser,
    ILogger<JiraSettingsService> logger) : IJiraSettingsService
{
    // A named purpose scopes the key: ciphertext from here cannot be decrypted by
    // another part of the app that happens to use Data Protection.
    private readonly IDataProtector _protector =
        protectionProvider.CreateProtector("SquadStatusBoard.JiraApiToken.v1");

    public async Task<JiraSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        var fromConfig = ConfiguredCredentials();

        if (settings is null)
        {
            return new JiraSettingsView(
                Configured: fromConfig is not null,
                Enabled: fromConfig is not null,
                BaseUrl: fromConfig?.BaseUrl ?? string.Empty,
                Email: fromConfig?.Email ?? string.Empty,
                TokenHint: fromConfig is null ? null : Mask(fromConfig.ApiToken),
                AutoApply: false,
                SyncIntervalMinutes: 0,
                UpdatedBy: null,
                UpdatedAt: null,
                LastSyncAt: null,
                LastSyncResult: null,
                OverriddenByConfiguration: fromConfig is not null);
        }

        return new JiraSettingsView(
            Configured: settings.IsUsable || fromConfig is not null,
            Enabled: settings.Enabled || fromConfig is not null,
            BaseUrl: fromConfig?.BaseUrl ?? settings.BaseUrl,
            Email: fromConfig?.Email ?? settings.Email,
            TokenHint: fromConfig is not null ? Mask(fromConfig.ApiToken) : NullIfBlank(settings.TokenHint),
            AutoApply: settings.AutoApply,
            SyncIntervalMinutes: settings.SyncIntervalMinutes,
            UpdatedBy: settings.UpdatedBy,
            UpdatedAt: settings.UpdatedAt,
            LastSyncAt: settings.LastSyncAt,
            LastSyncResult: settings.LastSyncResult,
            OverriddenByConfiguration: fromConfig is not null);
    }

    public async Task<JiraSettingsView> SaveAsync(SaveJiraSettings request,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        var (cipher, hint) = string.IsNullOrWhiteSpace(request.ApiToken)
            ? (string.Empty, string.Empty)
            : (_protector.Protect(request.ApiToken.Trim()), Mask(request.ApiToken.Trim()));

        if (settings is null)
        {
            settings = new JiraSettings(request.BaseUrl, request.Email, cipher, hint,
                request.Enabled, currentUser.DisplayName);
            db.JiraSettings.Add(settings);
        }
        else
        {
            settings.Update(request.BaseUrl, request.Email, cipher, hint,
                request.Enabled, currentUser.DisplayName);
        }

        settings.ConfigureSync(request.AutoApply, request.SyncIntervalMinutes);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Jira connection updated by {User} (enabled={Enabled}, autoApply={AutoApply}, " +
            "interval={Interval}m).",
            currentUser.DisplayName, settings.Enabled, settings.AutoApply,
            settings.SyncIntervalMinutes);

        return await GetAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        db.JiraSettings.Remove(settings);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Jira connection removed by {User}.", currentUser.DisplayName);
    }

    public async Task<JiraCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        // Configuration wins, so a pinned deployment cannot be redirected from the UI.
        var fromConfig = ConfiguredCredentials();
        if (fromConfig is not null) return fromConfig;

        var settings = await LoadAsync(cancellationToken);
        if (settings is null || !settings.IsUsable) return null;

        try
        {
            return new JiraCredentials(
                settings.BaseUrl,
                settings.Email,
                _protector.Unprotect(settings.EncryptedApiToken));
        }
        catch (Exception ex)
        {
            // Happens when the Data Protection key ring is lost — for instance a
            // container without a persisted key directory. Say so plainly: the fix is
            // to re-enter the token, not to debug a decryption error.
            logger.LogError(ex,
                "The stored Jira token could not be decrypted. The Data Protection keys " +
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

    private Task<JiraSettings?> LoadAsync(CancellationToken cancellationToken) =>
        db.JiraSettings.FirstOrDefaultAsync(s => s.Id == JiraSettings.SingletonId, cancellationToken);

    /// <summary>Credentials from environment configuration, when a deployment pins them.</summary>
    private JiraCredentials? ConfiguredCredentials()
    {
        var section = configuration.GetSection("Jira");

        var baseUrl = section["BaseUrl"]?.TrimEnd('/');
        var email = section["Email"];
        var token = section["ApiToken"];

        return section.GetValue("Enabled", false)
               && !string.IsNullOrWhiteSpace(baseUrl)
               && !string.IsNullOrWhiteSpace(email)
               && !string.IsNullOrWhiteSpace(token)
            ? new JiraCredentials(baseUrl, email, token)
            : null;
    }

    /// <summary>Shows only the last four characters — enough to identify, not to use.</summary>
    private static string Mask(string token) =>
        token.Length <= 4
            ? new string('•', 8)
            : new string('•', 8) + token[^4..];

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
