using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// One person's relationship with one conversation: when they last read it, and whether
/// they want to hear about it.
///
/// For a direct conversation this row *is* access — the two rows are the two people. For
/// board and squad channels it is only bookkeeping: access is decided from the board,
/// so removing this row hides the channel from someone's inbox without hiding the board.
/// </summary>
public class ConversationMember : Entity
{
    private ConversationMember() { }

    public ConversationMember(Guid conversationId, Guid userId)
    {
        ConversationId = conversationId;
        UserId = userId;
        JoinedAt = DateTimeOffset.UtcNow;
        LastReadAt = null;
        IsMuted = false;
    }

    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;

    public Guid UserId { get; private set; }
    public AppUser User { get; private set; } = null!;

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>
    /// When this person last had the thread open. Null means they have never read it, and
    /// null is deliberately *not* treated as "everything is unread" — see
    /// <see cref="UnreadSince"/>.
    /// </summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>Muted channels still collect messages; they just stop counting.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>
    /// The instant unread is measured from.
    ///
    /// Someone joining a channel with two years of history has not "missed" 4,000
    /// messages, and telling them so is the fastest way to make them mute it. Unread
    /// starts at the moment they joined.
    /// </summary>
    public DateTimeOffset UnreadSince => LastReadAt ?? JoinedAt;

    /// <summary>
    /// Marks the thread read up to an instant.
    ///
    /// Monotonic on purpose: two tabs open, the stale one reporting an older timestamp,
    /// must not resurrect messages the person has already seen.
    /// </summary>
    public void MarkReadAt(DateTimeOffset readAt)
    {
        if (LastReadAt is { } current && readAt <= current) return;
        LastReadAt = readAt;
    }

    public void SetMuted(bool muted) => IsMuted = muted;
}
