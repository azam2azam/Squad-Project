using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations;

/// <summary>
/// The Telegram Bot API, over long polling.
///
/// Long polling rather than a webhook on purpose. A webhook needs Telegram to be able to
/// reach this application, which means a public HTTPS address and a hole in the firewall —
/// for an internal delivery board that is a lot of infrastructure to ask for. Polling
/// needs only outbound access to api.telegram.org, which the office network already
/// allows, and it works identically on a laptop and in a data centre.
///
/// Credentials come from <see cref="ITelegramSettingsService"/> on every call rather than
/// being captured at construction, so saving the settings screen takes effect immediately.
/// </summary>
public sealed class TelegramClient(
    HttpClient http,
    ITelegramSettingsService settings,
    ILogger<TelegramClient> logger) : ITelegramClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        await settings.GetCredentialsAsync(cancellationToken) is not null;

    public async Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(long offset,
        int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);
        if (credentials is null) return [];

        // allowed_updates keeps the bot from being handed edits, reactions, poll answers
        // and everything else it has no opinion about.
        var url = $"{Method(credentials, "getUpdates")}"
                  + $"?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";

        try
        {
            using var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram getUpdates returned {Status}.", (int)response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream,
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return result.EnumerateArray().Select(Read).OfType<TelegramInboundMessage>().ToList();
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-poll is normal, not a failure worth logging.
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Telegram getUpdates failed; will retry.");
            return [];
        }
    }

    public async Task<bool> SendMessageAsync(long chatId, string text,
        CancellationToken cancellationToken = default)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);
        if (credentials is null || string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            var payload = JsonSerializer.Serialize(new { chat_id = chatId, text }, Json);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Method(credentials, "sendMessage"),
                content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Worth a log but never worth failing the update it was replying about: the
                // board has already been changed, and undoing it because a reply bounced
                // would be the wrong trade.
                logger.LogWarning("Telegram sendMessage to {ChatId} returned {Status}.",
                    chatId, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Telegram sendMessage to {ChatId} failed.", chatId);
            return false;
        }
    }

    public async Task<TelegramBotIdentity?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);
        if (credentials is null) return null;

        try
        {
            using var response = await http.GetAsync(Method(credentials, "getMe"), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream,
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("result", out var result)) return null;

            return new TelegramBotIdentity(
                result.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                Text(result, "username") ?? "unknown",
                Text(result, "first_name") ?? "Bot");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Telegram getMe failed.");
            return null;
        }
    }

    /// <summary>
    /// The token lives in the path, which is why the base URL is forced to https and why
    /// nothing here ever logs the composed URL.
    /// </summary>
    private static string Method(TelegramCredentials credentials, string method) =>
        $"{credentials.BaseUrl}/bot{credentials.BotToken}/{method}";

    /// <summary>
    /// Flattens one update. Anything that is not a text message from a real user — a photo,
    /// a channel post, a bot's own message — is dropped here rather than downstream.
    /// </summary>
    private static TelegramInboundMessage? Read(JsonElement update)
    {
        if (!update.TryGetProperty("update_id", out var updateIdElement)) return null;

        if (!update.TryGetProperty("message", out var message)
            || !message.TryGetProperty("text", out var textElement)
            || !message.TryGetProperty("from", out var from)
            || !message.TryGetProperty("chat", out var chat))
        {
            return null;
        }

        if (from.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var name = string.Join(' ', new[] { Text(from, "first_name"), Text(from, "last_name") }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new TelegramInboundMessage(
            updateIdElement.GetInt64(),
            chat.TryGetProperty("id", out var chatId) ? chatId.GetInt64() : 0,
            Text(chat, "type") ?? "private",
            from.TryGetProperty("id", out var fromId) ? fromId.GetInt64() : 0,
            string.IsNullOrWhiteSpace(name) ? Text(from, "username") ?? "Unknown" : name,
            Text(from, "username"),
            textElement.GetString() ?? string.Empty);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
