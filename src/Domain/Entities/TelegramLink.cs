using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// The join between a Telegram account and an application account.
///
/// This is the whole security model of the integration. A bot's username is public and
/// anybody can message it, so "who sent this" cannot come from the message — it has to
/// come from a link somebody deliberately established. An unlinked sender can do nothing
/// at all, and a linked sender can do exactly what their application role already lets
/// them do, no more.
/// </summary>
public class TelegramLink : Entity
{
    private TelegramLink() { }

    public TelegramLink(long telegramUserId, string? telegramUsername, string displayName,
        Guid userId)
    {
        TelegramUserId = telegramUserId;
        TelegramUsername = Clean(telegramUsername);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Telegram user" : displayName.Trim();
        UserId = userId;
        LinkedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    /// <summary>Telegram's own numeric id for the person. Stable, unlike a username.</summary>
    public long TelegramUserId { get; private set; }

    /// <summary>@handle, for display only — people change these.</summary>
    public string? TelegramUsername { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }
    public AppUser? User { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Refreshes the display fields, which Telegram sends on every message.</summary>
    public void Seen(string? telegramUsername, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(telegramUsername)) TelegramUsername = Clean(telegramUsername);
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();

        LastSeenAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Revoked rather than deleted, so the message log still says who sent what. A revoked
    /// link is refused exactly like an unknown sender.
    /// </summary>
    public void Revoke()
    {
        IsActive = false;
        RevokedAt = DateTimeOffset.UtcNow;
    }

    public void Restore(Guid userId)
    {
        UserId = userId;
        IsActive = true;
        RevokedAt = null;
        LinkedAt = DateTimeOffset.UtcNow;
    }

    private static string? Clean(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');
}

/// <summary>
/// A one-time code that turns into a <see cref="TelegramLink"/> when somebody sends it to
/// the bot.
///
/// Short-lived and single-use on purpose: it is typed into a chat window, where it will sit
/// in somebody's history forever, so its value has to expire quickly and be worthless once
/// redeemed.
/// </summary>
public class TelegramEnrolment : Entity
{
    /// <summary>Long enough not to be guessed in the window it is alive for.</summary>
    public const int CodeLength = 8;

    private TelegramEnrolment() { }

    public TelegramEnrolment(string code, Guid userId, string issuedBy, TimeSpan validFor)
    {
        Code = code.Trim().ToUpperInvariant();
        UserId = userId;
        IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "system" : issuedBy.Trim();
        IssuedAt = DateTimeOffset.UtcNow;
        ExpiresAt = IssuedAt.Add(validFor);
    }

    public string Code { get; private set; } = string.Empty;

    /// <summary>The application account this code will link a Telegram user to.</summary>
    public Guid UserId { get; private set; }
    public AppUser? User { get; private set; }

    public string IssuedBy { get; private set; } = "system";
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public long? RedeemedByTelegramUserId { get; private set; }

    public bool IsRedeemed => RedeemedAt is not null;

    public bool IsUsable(DateTimeOffset now) => !IsRedeemed && now < ExpiresAt;

    public void Redeem(long telegramUserId)
    {
        RedeemedAt = DateTimeOffset.UtcNow;
        RedeemedByTelegramUserId = telegramUserId;
    }
}
