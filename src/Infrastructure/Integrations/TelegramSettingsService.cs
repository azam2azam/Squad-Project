using Application.Abstractions;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// Stores the Telegram connection in the database, with the bot token encrypted through
/// ASP.NET Core Data Protection.
///
/// Mirrors <see cref="JiraSettingsService"/> and <see cref="SmartsheetSettingsService"/>,
/// including the rule that configuration wins: if Telegram__BotToken is set in the
/// environment, that is what the client uses and the settings screen says so.
///
/// The token deserves the same care as the other two for a different reason. A Jira token
/// reads; a Telegram bot token <em>is</em> the bot — whoever holds it can read every
/// message sent to it and answer as the company's bot.
/// </summary>
public sealed class TelegramSettingsService(
    IAppDbContext db,
    IDataProtectionProvider protectionProvider,
    IConfiguration configuration,
    ICurrentUser currentUser,
    ILogger<TelegramSettingsService> logger) : ITelegramSettingsService
{
    // A named purpose scopes the key: ciphertext from here cannot be decrypted by another
    // part of the app that happens to use Data Protection.
    private readonly IDataProtector _protector =
        protectionProvider.CreateProtector("SquadStatusBoard.TelegramBotToken.v1");

    public async Task<TelegramSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        var fromConfig = ConfiguredCredentials();

        if (settings is null)
        {
            return new TelegramSettingsView(
                Configured: fromConfig is not null,
                Enabled: fromConfig is not null,
                BaseUrl: fromConfig?.BaseUrl ?? TelegramSettings.DefaultBaseUrl,
                TokenHint: fromConfig is null ? null : Mask(fromConfig.BotToken),
                BotUsername: null,
                AllowedChatIds: null,
                ReplyToUnknownSenders: true,
                LastUpdateId: 0,
                UpdatedBy: null,
                UpdatedAt: null,
                LastPollAt: null,
                LastPollResult: null,
                OverriddenByConfiguration: fromConfig is not null);
        }

        return new TelegramSettingsView(
            Configured: settings.IsUsable || fromConfig is not null,
            Enabled: settings.Enabled || fromConfig is not null,
            BaseUrl: fromConfig?.BaseUrl ?? settings.BaseUrl,
            TokenHint: fromConfig is not null ? Mask(fromConfig.BotToken) : NullIfBlank(settings.TokenHint),
            BotUsername: settings.BotUsername,
            AllowedChatIds: settings.AllowedChatIds,
            ReplyToUnknownSenders: settings.ReplyToUnknownSenders,
            LastUpdateId: settings.LastUpdateId,
            UpdatedBy: settings.UpdatedBy,
            UpdatedAt: settings.UpdatedAt,
            LastPollAt: settings.LastPollAt,
            LastPollResult: settings.LastPollResult,
            OverriddenByConfiguration: fromConfig is not null);
    }

    public async Task<TelegramSettingsView> SaveAsync(SaveTelegramSettings request,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        var (cipher, hint) = string.IsNullOrWhiteSpace(request.BotToken)
            ? (string.Empty, string.Empty)
            : (_protector.Protect(request.BotToken.Trim()), Mask(request.BotToken.Trim()));

        if (settings is null)
        {
            settings = new TelegramSettings(request.BaseUrl, cipher, hint, request.Enabled,
                currentUser.DisplayName);
            db.TelegramSettings.Add(settings);
        }
        else
        {
            settings.Update(request.BaseUrl, cipher, hint, request.Enabled, currentUser.DisplayName);
        }

        settings.ConfigureDelivery(request.AllowedChatIds, request.ReplyToUnknownSenders);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Telegram connection updated by {User} (enabled={Enabled}).",
            currentUser.DisplayName, settings.Enabled);

        return await GetAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        db.TelegramSettings.Remove(settings);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Telegram connection removed by {User}.", currentUser.DisplayName);
    }

    public async Task<TelegramCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        // Configuration wins, so a pinned deployment cannot be redirected from the UI. The
        // offset still comes from the database — it is state, not configuration.
        var fromConfig = ConfiguredCredentials();
        if (fromConfig is not null)
        {
            return fromConfig with { LastUpdateId = settings?.LastUpdateId ?? 0 };
        }

        if (settings is null || !settings.IsUsable) return null;

        try
        {
            return new TelegramCredentials(
                settings.BaseUrl,
                _protector.Unprotect(settings.EncryptedBotToken),
                settings.LastUpdateId);
        }
        catch (Exception ex)
        {
            // Happens when the Data Protection key ring is lost — for instance a container
            // without a persisted key directory. Say so plainly: the fix is to re-enter the
            // token, not to debug a decryption error.
            logger.LogError(ex,
                "The stored Telegram bot token could not be decrypted. The Data Protection keys " +
                "have probably changed; re-enter the token in Settings.");
            return null;
        }
    }

    public async Task AcknowledgeAsync(long updateId, string result,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        settings.AcknowledgeUpdates(updateId);
        settings.RecordPoll(result);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordBotUsernameAsync(string? username,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is null) return;

        settings.RecordBotUsername(username);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<TelegramSettings?> LoadAsync(CancellationToken cancellationToken) =>
        db.TelegramSettings.FirstOrDefaultAsync(
            s => s.Id == TelegramSettings.SingletonId, cancellationToken);

    private TelegramCredentials? ConfiguredCredentials()
    {
        var section = configuration.GetSection("Telegram");

        var baseUrl = section["BaseUrl"]?.TrimEnd('/');
        var token = section["BotToken"];

        return section.GetValue("Enabled", false) && !string.IsNullOrWhiteSpace(token)
            ? new TelegramCredentials(
                string.IsNullOrWhiteSpace(baseUrl) ? TelegramSettings.DefaultBaseUrl : baseUrl,
                token,
                0)
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
