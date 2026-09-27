using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// A message naming a particular person.
///
/// Stored rather than re-parsed on read, for two reasons. A mention has to survive the
/// person being renamed — the message text keeps the old spelling, the notification still
/// goes to the right inbox. And "everything that mentions me" is the one query in this
/// feature people run constantly, which wants an index, not a regex over every row.
/// </summary>
public class MessageMention : Entity
{
    private MessageMention() { }

    public MessageMention(Guid messageId, Guid mentionedUserId)
    {
        MessageId = messageId;
        MentionedUserId = mentionedUserId;
    }

    public Guid MessageId { get; private set; }
    public Message Message { get; private set; } = null!;

    public Guid MentionedUserId { get; private set; }
    public AppUser MentionedUser { get; private set; } = null!;
}
