using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A thread of messages, attached to a board, a squad, or another person.
///
/// Conversations are created on demand rather than up front: a board gets its channel the
/// first time somebody opens it, not the moment the board is created. Thirty boards and
/// fourteen squads would otherwise produce forty-four empty channels on day one, and an
/// inbox of empty channels is one nobody scrolls past.
///
/// Messages are deliberately *not* a navigation collection here. EF assigns this
/// aggregate's GUID in the constructor, so an untracked child reached through a tracked
/// parent is marked Modified rather than Added and EF then updates a row that does not
/// exist. Handlers add to <c>db.Messages</c> explicitly and call
/// <see cref="RecordMessage"/> to move the summary fields.
/// </summary>
public class Conversation : Entity
{
    private readonly List<ConversationMember> _members = [];

    private Conversation() { }

    private Conversation(ConversationKind kind, string title, string scopeKey,
        Guid? boardId, string? squadName)
    {
        Kind = kind;
        Title = title;
        ScopeKey = scopeKey;
        BoardId = boardId;
        SquadName = squadName;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = CreatedAt;
    }

    public ConversationKind Kind { get; private set; }

    /// <summary>
    /// What this conversation is called in a list. Denormalised from the board or squad
    /// so the inbox renders without joining three tables per row, and refreshed by
    /// <see cref="Rename"/> when the board is retitled.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// The uniqueness key for this conversation, unique across the whole table.
    ///
    /// One string rather than three nullable columns because the invariant being protected
    /// is a single one — *there is at most one conversation per thing* — and a unique index
    /// over one non-null column states that in a way the database can actually enforce.
    /// </summary>
    public string ScopeKey { get; private set; } = string.Empty;

    /// <summary>Set for <see cref="ConversationKind.Board"/> only.</summary>
    public Guid? BoardId { get; private set; }

    public Board? Board { get; private set; }

    /// <summary>
    /// Set for <see cref="ConversationKind.Squad"/> only, in the squad's own casing for
    /// display. Matching is done on <see cref="ScopeKey"/>, which is case-folded.
    /// </summary>
    public string? SquadName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the last message landed. Drives inbox ordering, so it is stored rather than
    /// computed — sorting an inbox by a subquery maximum gets slow exactly when the
    /// product is being used most.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>First line of the last message, for the inbox. Null until someone writes.</summary>
    public string? LastMessagePreview { get; private set; }

    public string? LastMessageAuthor { get; private set; }

    public IReadOnlyCollection<ConversationMember> Members => _members;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    public static Conversation ForBoard(Guid boardId, string boardTitle) =>
        new(ConversationKind.Board, Clamp(boardTitle, "Board"), BoardScopeKey(boardId),
            boardId, null);

    public static Conversation ForSquad(string squadName)
    {
        var trimmed = (squadName ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainException("A squad conversation needs a squad name.");
        }

        return new Conversation(ConversationKind.Squad, trimmed, SquadScopeKey(trimmed),
            null, trimmed);
    }

    /// <summary>
    /// A two-person conversation. The title is left empty on purpose: a direct thread is
    /// named after *the other person*, which differs depending on who is looking at it,
    /// so the inbox resolves it per reader rather than storing one answer.
    /// </summary>
    public static Conversation ForDirect(Guid firstUserId, Guid secondUserId)
    {
        if (firstUserId == secondUserId)
        {
            throw new DomainException("A direct conversation needs two different people.");
        }

        var conversation = new Conversation(ConversationKind.Direct, string.Empty,
            DirectScopeKey(firstUserId, secondUserId), null, null);

        conversation.Join(firstUserId);
        conversation.Join(secondUserId);

        return conversation;
    }

    // -----------------------------------------------------------------------
    // Scope keys
    // -----------------------------------------------------------------------

    public static string BoardScopeKey(Guid boardId) => $"board:{boardId}";

    /// <summary>
    /// Case- and space-folded, because <c>SquadName</c> is free text typed on thirty
    /// different boards. "Pradeep &amp; Shehan" and "pradeep &amp; shehan" are one squad,
    /// and two channels for one squad is the failure this key exists to prevent.
    /// </summary>
    public static string SquadScopeKey(string squadName) =>
        $"squad:{string.Join(' ', (squadName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant()}";

    /// <summary>
    /// Order-independent, so A messaging B and B messaging A resolve to the same thread.
    /// Sorting the pair is what makes that true without a second lookup.
    /// </summary>
    public static string DirectScopeKey(Guid firstUserId, Guid secondUserId)
    {
        var (low, high) = firstUserId.CompareTo(secondUserId) <= 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);

        return $"direct:{low}:{high}";
    }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    /// <summary>
    /// Adds someone to the conversation, or returns their existing membership.
    ///
    /// For board and squad channels this happens the first time a user opens one: the
    /// membership row exists to track what they have read, not to grant access, which is
    /// decided from the board itself.
    /// </summary>
    public ConversationMember Join(Guid userId)
    {
        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null) return existing;

        if (Kind == ConversationKind.Direct && _members.Count >= 2)
        {
            throw new DomainException("A direct conversation is between two people only.");
        }

        var member = new ConversationMember(Id, userId);
        _members.Add(member);

        return member;
    }

    /// <summary>
    /// Leaves a board or squad channel. Direct conversations cannot be left — there is
    /// no version of a two-person thread with one person in it, and the honest way to
    /// stop hearing from someone is to mute.
    /// </summary>
    public void Leave(Guid userId)
    {
        if (Kind == ConversationKind.Direct)
        {
            throw new DomainException("A direct conversation cannot be left. Mute it instead.");
        }

        var member = _members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return;

        _members.Remove(member);
    }

    public bool HasMember(Guid userId) => _members.Any(m => m.UserId == userId);

    /// <summary>
    /// Moves the summary fields after a message is written. Called by the handler that
    /// adds the message, since the message itself is not owned by this aggregate.
    /// </summary>
    public void RecordMessage(string body, string authorName, DateTimeOffset sentAt)
    {
        LastActivityAt = sentAt;
        LastMessagePreview = Preview(body);
        LastMessageAuthor = authorName;
    }

    /// <summary>
    /// Keeps a board channel's title in step with the board. Called from the board update
    /// path: a channel still called by last month's board title is a channel people stop
    /// trusting they are in the right place for.
    /// </summary>
    public void Rename(string title)
    {
        if (Kind == ConversationKind.Direct) return;
        Title = Clamp(title, Title);
    }

    /// <summary>One line, because the inbox has one line. Whitespace is collapsed first.</summary>
    private static string Preview(string body)
    {
        var flat = string.Join(' ', (body ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length <= 140 ? flat : flat[..139] + "…";
    }

    private static string Clamp(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return fallback;
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
