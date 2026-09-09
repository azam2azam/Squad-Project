using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Every inbound message the bot acted on, or refused to.
///
/// Kept because an integration that changes portfolio numbers from a chat window has to be
/// answerable for what it did: which message, from whom, on which board, and what the
/// application made of it. The refusals matter as much as the successes — "my update never
/// arrived" is answered here, not by guesswork.
/// </summary>
public class TelegramMessage : Entity
{
    private TelegramMessage() { }

    public TelegramMessage(long updateId, long chatId, long senderTelegramUserId,
        string senderName, string text)
    {
        UpdateId = updateId;
        ChatId = chatId;
        SenderTelegramUserId = senderTelegramUserId;
        SenderName = string.IsNullOrWhiteSpace(senderName) ? "Unknown" : senderName.Trim();

        // Truncated rather than unbounded: this is a log, and somebody will eventually
        // paste a novel into the chat.
        Text = Truncate(text, 2000);

        ReceivedAt = DateTimeOffset.UtcNow;
        Outcome = TelegramOutcome.Received;
    }

    public long UpdateId { get; private set; }
    public long ChatId { get; private set; }
    public long SenderTelegramUserId { get; private set; }
    public string SenderName { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }

    public TelegramOutcome Outcome { get; private set; }

    /// <summary>What the reader needs to know: which fields changed, or why nothing did.</summary>
    public string? Detail { get; private set; }

    /// <summary>The board it landed on, when it landed on one.</summary>
    public Guid? BoardId { get; private set; }

    /// <summary>The application account the sender was acting as, when they were linked.</summary>
    public Guid? UserId { get; private set; }

    public void Resolve(TelegramOutcome outcome, string? detail, Guid? boardId = null,
        Guid? userId = null)
    {
        Outcome = outcome;
        Detail = Truncate(detail, 1000);
        BoardId = boardId ?? BoardId;
        UserId = userId ?? UserId;
    }

    private static string Truncate(string? value, int max)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
