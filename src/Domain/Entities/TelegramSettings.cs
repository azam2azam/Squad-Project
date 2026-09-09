using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// The connection to the company's Telegram bot, managed from the app rather than only
/// from deployment configuration.
///
/// Unlike Jira and Smartsheet this integration is <em>inbound</em>: nothing is polled for
/// figures, people send an update and the app applies it. That difference drives most of
/// what is here — an update offset instead of a sync interval, and a hard requirement that
/// the sender is a linked account rather than anyone who found the bot.
///
/// The bot token is held <b>encrypted</b> — see the settings service. This entity never
/// sees the plaintext and never hands it back out.
/// </summary>
public class TelegramSettings : Entity
{
    /// <summary>One bot per deployment, so the row has a fixed id.</summary>
    public static readonly Guid SingletonId = new("cf1a7e90-0000-4000-b000-00000000000f");

    /// <summary>Telegram's public Bot API. Only overridden for a proxy or a test double.</summary>
    public const string DefaultBaseUrl = "https://api.telegram.org";

    private TelegramSettings() { }

    public TelegramSettings(string baseUrl, string encryptedBotToken, string tokenHint,
        bool enabled, string updatedBy)
    {
        Id = SingletonId;
        Update(baseUrl, encryptedBotToken, tokenHint, enabled, updatedBy);
    }

    /// <summary>e.g. https://api.telegram.org — no trailing slash.</summary>
    public string BaseUrl { get; private set; } = DefaultBaseUrl;

    /// <summary>Ciphertext only. Never logged, never returned to a client.</summary>
    public string EncryptedBotToken { get; private set; } = string.Empty;

    /// <summary>
    /// Last four characters of the token, so an admin can confirm <em>which</em> bot is
    /// stored without the token being readable.
    /// </summary>
    public string TokenHint { get; private set; } = string.Empty;

    /// <summary>Filled in by Test connection, so the settings page can show @thebot.</summary>
    public string? BotUsername { get; private set; }

    /// <summary>Off by default: configuring a bot is not the same as turning it on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// The highest update id already processed. Telegram replays anything unacknowledged,
    /// so persisting this is what stops a restart re-applying yesterday's messages.
    /// </summary>
    public long LastUpdateId { get; private set; }

    /// <summary>
    /// Optional allow-list of chat ids. Empty means any chat, which is still safe because
    /// the sender must be linked — this exists for deployments that want updates to happen
    /// only in the one squad group everybody can see.
    /// </summary>
    public string? AllowedChatIds { get; private set; }

    /// <summary>
    /// When true the bot answers a message it cannot act on. Off means silence outside the
    /// allow-list, which is what you want when the bot sits in a busy group chat.
    /// </summary>
    public bool ReplyToUnknownSenders { get; private set; } = true;

    public string UpdatedBy { get; private set; } = "system";
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastPollAt { get; private set; }
    public string? LastPollResult { get; private set; }

    /// <summary>Usable means: switched on, and carrying everything a request needs.</summary>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(EncryptedBotToken);

    public void Update(string baseUrl, string encryptedBotToken, string tokenHint,
        bool enabled, string updatedBy)
    {
        SetBaseUrl(baseUrl);

        // An empty token means "leave the stored one alone" — the UI cannot send back a
        // value it was never given, so a blank field must not wipe a working connection.
        if (!string.IsNullOrWhiteSpace(encryptedBotToken))
        {
            EncryptedBotToken = encryptedBotToken;
            TokenHint = tokenHint;

            // A new token may well be a different bot; the username is re-read by Test
            // connection rather than left pointing at the old one.
            BotUsername = null;
        }

        Enabled = enabled;
        UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ConfigureDelivery(string? allowedChatIds, bool replyToUnknownSenders)
    {
        AllowedChatIds = string.IsNullOrWhiteSpace(allowedChatIds)
            ? null
            : string.Join(',', ParseChatIds(allowedChatIds).Select(id => id.ToString()));

        ReplyToUnknownSenders = replyToUnknownSenders;
    }

    public void RecordBotUsername(string? username) =>
        BotUsername = string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');

    /// <summary>
    /// Only ever moves forward. A lower offset would mean re-reading messages already
    /// applied, and applying an update twice is worse than dropping one.
    /// </summary>
    public void AcknowledgeUpdates(long updateId)
    {
        if (updateId > LastUpdateId) LastUpdateId = updateId;
    }

    public void RecordPoll(string result)
    {
        LastPollAt = DateTimeOffset.UtcNow;
        LastPollResult = result;
    }

    /// <summary>True when this chat may be acted on. An empty allow-list allows everything.</summary>
    public bool AllowsChat(long chatId)
    {
        if (string.IsNullOrWhiteSpace(AllowedChatIds)) return true;

        return ParseChatIds(AllowedChatIds).Contains(chatId);
    }

    private static IEnumerable<long> ParseChatIds(string value) =>
        value.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
             .Select(part => long.TryParse(part.Trim(), out var id) ? id : (long?)null)
             .Where(id => id.HasValue)
             .Select(id => id!.Value)
             .Distinct();

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
                "The Telegram API URL must be a full address, for example https://api.telegram.org");
        }

        // The bot token travels in the path of every request, so http would leak it.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new DomainException(
                "Use https for the Telegram API URL. The bot token is part of every request " +
                "path and can be read in transit over http.");
        }

        BaseUrl = trimmed;
    }
}
