using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Messaging;

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/// <summary>One row in the inbox.</summary>
public sealed record ConversationSummaryDto(
    Guid Id,
    ConversationKind Kind,
    string KindLabel,
    string Title,
    string? Subtitle,
    Guid? BoardId,
    string? SquadName,
    // For a direct thread, the other person. Null for channels.
    Guid? CounterpartUserId,
    string? CounterpartInitials,
    string? CounterpartColor,
    string? LastMessagePreview,
    string? LastMessageAuthor,
    DateTimeOffset LastActivityAt,
    int UnreadCount,
    int UnreadMentionCount,
    bool IsMuted,
    bool IsJoined);

public sealed record MessageDto(
    Guid Id,
    Guid AuthorUserId,
    string AuthorName,
    string AuthorInitials,
    string AuthorColor,
    Guid? AuthorPersonId,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? EditedAt,
    bool IsDeleted,
    bool IsMine,
    bool MentionsMe,
    Guid? ReplyToMessageId,
    string? ReplyToAuthorName,
    string? ReplyToExcerpt);

/// <summary>A thread, with enough context to render its header without a second call.</summary>
public sealed record ConversationThreadDto(
    Guid Id,
    ConversationKind Kind,
    string KindLabel,
    string Title,
    string? Subtitle,
    Guid? BoardId,
    string? SquadName,
    // Boards this squad runs. Empty for board and direct threads.
    IReadOnlyList<ConversationBoardRefDto> RelatedBoards,
    IReadOnlyList<ConversationParticipantDto> Participants,
    IReadOnlyList<MessageDto> Messages,
    bool IsMuted,
    bool CanPost);

public sealed record ConversationBoardRefDto(Guid Id, string Title, string Product, string StatusLabel);

public sealed record ConversationParticipantDto(
    Guid UserId,
    Guid? PersonId,
    string DisplayName,
    string Initials,
    string Color,
    string? Headline,
    bool HasAccount,
    DateTimeOffset? LastReadAt);

/// <summary>Somewhere a message could be sent — used by the "new message" picker.</summary>
public sealed record MessageTargetDto(
    string Kind,
    string Label,
    string? Sublabel,
    Guid? BoardId,
    string? SquadName,
    Guid? UserId,
    string? Initials,
    string? Color);

/// <summary>Counts for the navigation badge.</summary>
public sealed record UnreadSummaryDto(int Conversations, int Messages, int Mentions);

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

/// <summary>
/// The access rules, in one place so the six handlers cannot disagree about them.
///
/// The model is deliberately simple, and matches the rest of the product: boards are
/// readable by anyone signed in, so board and squad channels are too. A direct thread is
/// the only private thing here, and it is private absolutely — two members, no admin
/// override, no joining.
/// </summary>
public static class MessagingAccess
{
    public const string BoardKindLabel = "Board";
    public const string SquadKindLabel = "Squad";
    public const string DirectKindLabel = "Direct";

    public static string KindLabel(ConversationKind kind) => kind switch
    {
        ConversationKind.Board => BoardKindLabel,
        ConversationKind.Squad => SquadKindLabel,
        _ => DirectKindLabel
    };

    /// <summary>The signed-in user, or 401. Messaging has no anonymous surface at all.</summary>
    public static Guid RequireUserId(ICurrentUserContext currentUser) =>
        currentUser.UserId
        ?? throw new UnauthorizedException("You must be signed in to use messages.");

    /// <summary>
    /// Throws unless this user may see this conversation.
    ///
    /// An administrator is not exempt on a direct thread. Being able to read two
    /// colleagues' private conversation is not a power this product needs, and building
    /// it in "just in case" is how a tool people trust becomes one they route around.
    /// </summary>
    public static void EnsureCanRead(Conversation conversation, Guid userId)
    {
        if (conversation.Kind != ConversationKind.Direct) return;

        if (!conversation.HasMember(userId))
        {
            throw new ForbiddenException("That conversation is between two other people.");
        }
    }

    /// <summary>
    /// Loads a conversation with its membership, or 404.
    /// Tracked, because every caller of this goes on to change something.
    /// </summary>
    public static async Task<Conversation> LoadAsync(IAppDbContext db, Guid conversationId,
        CancellationToken cancellationToken) =>
        await db.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
        ?? throw new KeyNotFoundException("That conversation was not found.");

    /// <summary>
    /// Joins a conversation, registering a newly created membership with the context.
    ///
    /// <see cref="Entity"/> assigns its own id in the constructor, so a child reached
    /// through an already-tracked parent is marked Modified rather than Added — EF then
    /// issues an UPDATE against a row that does not exist and throws
    /// <c>DbUpdateConcurrencyException</c>. Adding it to the set explicitly is the fix,
    /// and it lives here so no handler has to remember.
    /// </summary>
    public static ConversationMember Join(IAppDbContext db, Conversation conversation, Guid userId)
    {
        var before = conversation.Members.Count;
        var member = conversation.Join(userId);

        if (conversation.Members.Count > before)
        {
            db.ConversationMembers.Add(member);
        }

        return member;
    }

    /// <summary>
    /// Finds the squad name as a board actually spells it, matching however the caller
    /// typed it.
    ///
    /// Done in memory rather than in SQL because the match has to fold case *and*
    /// internal whitespace, exactly as <see cref="Conversation.SquadScopeKey"/> does, and
    /// a database comparison that folds only case silently disagrees with the key — which
    /// is how one squad ends up with two channels. There are tens of boards, not
    /// millions, so the distinct list is cheap.
    /// </summary>
    public static async Task<string?> ResolveSquadNameAsync(IAppDbContext db, string typed,
        CancellationToken cancellationToken)
    {
        var wanted = Conversation.SquadScopeKey(typed);

        var names = await db.Boards
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .Select(b => b.SquadName)
            .Distinct()
            .ToListAsync(cancellationToken);

        return names.FirstOrDefault(n => Conversation.SquadScopeKey(n) == wanted);
    }
}
