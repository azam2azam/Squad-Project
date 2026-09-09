namespace Application.Abstractions;

/// <summary>
/// Reads and writes the Telegram connection, handling encryption of the bot token.
///
/// The plaintext token exists in exactly two places: the moment an admin submits it, and
/// the moment the HTTP client builds a request URL. It is never returned to a client,
/// never logged, and never stored in the clear.
/// </summary>
public interface ITelegramSettingsService
{
    /// <summary>The connection as an admin should see it — token masked, never decrypted.</summary>
    Task<TelegramSettingsView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the connection. A blank token keeps the stored one, so re-saving the form
    /// without retyping the secret does not wipe a working bot.
    /// </summary>
    Task<TelegramSettingsView> SaveAsync(SaveTelegramSettings settings,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The credentials the HTTP client needs, or null when Telegram is not usable — so a
    /// caller cannot accidentally build a half-configured request.
    /// </summary>
    Task<TelegramCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves the polling offset forward and stamps the poll result.</summary>
    Task AcknowledgeAsync(long updateId, string result, CancellationToken cancellationToken = default);

    Task RecordBotUsernameAsync(string? username, CancellationToken cancellationToken = default);
}

/// <summary>What an admin sees. Deliberately has no field that could carry the token.</summary>
public sealed record TelegramSettingsView(
    bool Configured,
    bool Enabled,
    string BaseUrl,
    string? TokenHint,
    string? BotUsername,
    string? AllowedChatIds,
    bool ReplyToUnknownSenders,
    long LastUpdateId,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastPollAt,
    string? LastPollResult,
    bool OverriddenByConfiguration);

public sealed record SaveTelegramSettings(
    string BaseUrl,
    string? BotToken,
    bool Enabled,
    string? AllowedChatIds,
    bool ReplyToUnknownSenders);

/// <summary>Decrypted credentials plus the polling offset. Used only to build a request.</summary>
public sealed record TelegramCredentials(string BaseUrl, string BotToken, long LastUpdateId);

/// <summary>
/// The slice of the Telegram Bot API this application uses: read messages, answer them,
/// and confirm which bot the token belongs to. It never joins chats, never reads history,
/// and never sends anything nobody asked for.
/// </summary>
public interface ITelegramClient
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Long-polls for messages after <paramref name="offset"/>. Returns an empty list when
    /// the window closes with nothing new, which is the normal case.
    /// </summary>
    Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset, int timeoutSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>Answers one chat. Failure to reply never fails the update it was replying about.</summary>
    Task<bool> SendMessageAsync(long chatId, string text,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms the token works and says which bot it belongs to.</summary>
    Task<TelegramBotIdentity?> GetMeAsync(CancellationToken cancellationToken = default);
}

/// <summary>One message, flattened to the parts this application cares about.</summary>
public sealed record TelegramInboundMessage(
    long UpdateId,
    long ChatId,
    string ChatType,
    long SenderId,
    string SenderName,
    string? SenderUsername,
    string Text);

public sealed record TelegramBotIdentity(long Id, string Username, string Name);
