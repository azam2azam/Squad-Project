using System.Text.RegularExpressions;
using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// One message in a conversation.
///
/// Edits and deletions are marks, not erasures. A squad conversation is where a decision
/// gets made — "we are slipping the date", "the blocker is cleared" — and a thread that
/// can be silently rewritten afterwards is worthless as a record of it. An edited message
/// says so; a deleted one leaves a tombstone.
/// </summary>
public partial class Message : Entity
{
    private readonly List<MessageMention> _mentions = [];

    private Message() { }

    public Message(Guid conversationId, Guid authorUserId, string body,
        Guid? replyToMessageId = null)
    {
        ConversationId = conversationId;
        AuthorUserId = authorUserId;
        Body = Validate(body);
        ReplyToMessageId = replyToMessageId;
        SentAt = DateTimeOffset.UtcNow;
    }

    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;

    public Guid AuthorUserId { get; private set; }
    public AppUser Author { get; private set; } = null!;

    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// The message being replied to, if any. One level: threads inside threads turn a
    /// conversation into a tree nobody can read back in a standup.
    /// </summary>
    public Guid? ReplyToMessageId { get; private set; }

    public Message? ReplyTo { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Set when withdrawn. The row stays so replies above it still make sense.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public IReadOnlyCollection<MessageMention> Mentions => _mentions;

    /// <summary>
    /// Rewrites the body. Only the author may do this, which the handler enforces —
    /// the entity cannot see who is asking.
    /// </summary>
    public void Edit(string body)
    {
        if (IsDeleted)
        {
            throw new DomainException("A deleted message cannot be edited.");
        }

        Body = Validate(body);
        EditedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Withdraws the message. The body is cleared here rather than left for the client to
    /// hide: a body that still exists in the database is one that still leaks through an
    /// export, a search index or a stray API response.
    /// </summary>
    public void Delete()
    {
        if (IsDeleted) return;

        Body = string.Empty;
        DeletedAt = DateTimeOffset.UtcNow;
        _mentions.Clear();
    }

    /// <summary>
    /// Records that this message names someone. Resolution from text to user happens in
    /// the handler, which is the only layer that can see the roster.
    /// </summary>
    public void AddMention(Guid userId)
    {
        if (_mentions.Any(m => m.MentionedUserId == userId)) return;
        _mentions.Add(new MessageMention(Id, userId));
    }

    /// <summary>
    /// The names written in a body, without the leading @.
    ///
    /// One inner list per "@", holding that mention's possible readings from longest to
    /// shortest. Names here contain spaces, hyphens and non-ASCII letters, and the text
    /// gives no clue where the name stops: "@Sara Al-Otaibi can you confirm" could be
    /// four words or two. Offering every prefix and letting the caller take the longest
    /// one that is a real person is the only way to read it correctly — matching the
    /// greedy span alone finds nobody, and matching the first word alone finds the wrong
    /// Sara.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseMentionCandidates(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var occurrences = new List<IReadOnlyList<string>>();

        foreach (Match match in MentionPattern().Matches(body))
        {
            var words = match.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (words.Length == 0) continue;

            var readings = new List<string>();

            for (var take = words.Length; take > 0; take--)
            {
                readings.Add(string.Join(' ', words.Take(take)));
            }

            occurrences.Add(readings);
        }

        return occurrences;
    }

    [GeneratedRegex(@"@([\p{L}\p{M}][\p{L}\p{M}'\-\.]*(?:\s+[\p{L}\p{M}][\p{L}\p{M}'\-\.]*){0,3})")]
    private static partial Regex MentionPattern();

    private static string Validate(string body)
    {
        var trimmed = (body ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainException("A message cannot be empty.");
        }

        // Long enough for a considered update, short enough that the thread stays a
        // conversation rather than a document someone should have written instead.
        if (trimmed.Length > 4000)
        {
            throw new DomainException("A message cannot be longer than 4000 characters.");
        }

        return trimmed;
    }
}
