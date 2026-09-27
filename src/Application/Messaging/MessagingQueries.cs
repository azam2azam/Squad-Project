using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Messaging;

// ---------------------------------------------------------------------------
// The inbox
// ---------------------------------------------------------------------------

/// <summary>
/// Every conversation this person is in, newest activity first.
///
/// Only conversations they have actually joined. A product that lists all thirty board
/// channels on day one buries the two that matter to you, so channels arrive in your
/// inbox when you open them and leave when you leave them.
/// </summary>
public sealed record GetInboxQuery(string? Search = null) : IRequest<IReadOnlyList<ConversationSummaryDto>>;

public sealed class GetInboxQueryHandler(IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<GetInboxQuery, IReadOnlyList<ConversationSummaryDto>>
{
    public async Task<IReadOnlyList<ConversationSummaryDto>> Handle(
        GetInboxQuery request, CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var rows = await db.ConversationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new
            {
                m.Conversation.Id,
                m.Conversation.Kind,
                m.Conversation.Title,
                m.Conversation.BoardId,
                m.Conversation.SquadName,
                m.Conversation.LastMessagePreview,
                m.Conversation.LastMessageAuthor,
                m.Conversation.LastActivityAt,
                m.IsMuted,
                m.LastReadAt,
                m.JoinedAt,
                BoardProduct = db.Boards.Where(b => b.Id == m.Conversation.BoardId)
                    .Select(b => b.Product).FirstOrDefault(),
                SquadBoardCount = m.Conversation.SquadName == null
                    ? 0
                    : db.Boards.Count(b => !b.IsDeleted
                                           && b.SquadName.ToUpper() == m.Conversation.SquadName.ToUpper()),
                // The other person in a direct thread, resolved per reader.
                Counterpart = m.Conversation.Members
                    .Where(x => x.UserId != userId)
                    .Select(x => new
                    {
                        x.UserId,
                        Name = db.People.Where(p => p.Id == x.User.PersonId)
                            .Select(p => p.FullName).FirstOrDefault() ?? x.User.DisplayName,
                        Color = db.People.Where(p => p.Id == x.User.PersonId)
                            .Select(p => p.AvatarColorOverride).FirstOrDefault(),
                        Role = db.People.Where(p => p.Id == x.User.PersonId)
                            .Select(p => (Role?)p.DefaultRole).FirstOrDefault()
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var summaries = new List<ConversationSummaryDto>(rows.Count);

        foreach (var r in rows)
        {
            var since = r.LastReadAt ?? r.JoinedAt;

            // Counted per row rather than in the projection above: EF cannot translate a
            // correlated count against a value computed in the same Select, and folding
            // it in produced a client-evaluation warning rather than a query.
            var unread = await db.Messages.AsNoTracking()
                .CountAsync(x => x.ConversationId == r.Id
                                 && x.SentAt > since
                                 && x.DeletedAt == null
                                 && x.AuthorUserId != userId, cancellationToken);

            var unreadMentions = await db.MessageMentions.AsNoTracking()
                .CountAsync(x => x.MentionedUserId == userId
                                 && x.Message.ConversationId == r.Id
                                 && x.Message.SentAt > since
                                 && x.Message.DeletedAt == null, cancellationToken);

            var title = r.Kind == ConversationKind.Direct
                ? r.Counterpart?.Name ?? "Direct message"
                : r.Title;

            var subtitle = r.Kind switch
            {
                ConversationKind.Board => r.BoardProduct,
                ConversationKind.Squad => r.SquadBoardCount == 1
                    ? "1 board"
                    : $"{r.SquadBoardCount} boards",
                _ => null
            };

            summaries.Add(new ConversationSummaryDto(
                r.Id,
                r.Kind,
                MessagingAccess.KindLabel(r.Kind),
                title,
                subtitle,
                r.BoardId,
                r.SquadName,
                r.Counterpart?.UserId,
                r.Counterpart is null ? null : Person.ComputeInitials(r.Counterpart.Name),
                r.Counterpart?.Color
                ?? (r.Counterpart?.Role is { } cr ? RoleMetadata.Color(cr) : null),
                r.LastMessagePreview,
                r.LastMessageAuthor,
                r.LastActivityAt,
                unread,
                unreadMentions,
                r.IsMuted,
                true));
        }

        var term = request.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(term))
        {
            summaries = summaries
                .Where(s => s.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || (s.LastMessagePreview?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        // Unread first, then by recency. An inbox sorted purely by time makes you scroll
        // past yesterday's settled threads to find the one asking you a question.
        return summaries
            .OrderByDescending(s => s.UnreadMentionCount > 0)
            .ThenByDescending(s => s.UnreadCount > 0 && !s.IsMuted)
            .ThenByDescending(s => s.LastActivityAt)
            .ToList();
    }
}

/// <summary>The badge on the navigation rail.</summary>
public sealed record GetUnreadSummaryQuery : IRequest<UnreadSummaryDto>;

public sealed class GetUnreadSummaryQueryHandler(IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<GetUnreadSummaryQuery, UnreadSummaryDto>
{
    public async Task<UnreadSummaryDto> Handle(GetUnreadSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var memberships = await db.ConversationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.ConversationId, m.IsMuted, m.LastReadAt, m.JoinedAt })
            .ToListAsync(cancellationToken);

        var conversations = 0;
        var messages = 0;
        var mentions = 0;

        foreach (var m in memberships)
        {
            var since = m.LastReadAt ?? m.JoinedAt;

            // Mentions count even when muted. Muting says "stop counting the chatter",
            // not "hide it when somebody asks me a direct question".
            var mentionCount = await db.MessageMentions.AsNoTracking()
                .CountAsync(x => x.MentionedUserId == userId
                                 && x.Message.ConversationId == m.ConversationId
                                 && x.Message.SentAt > since
                                 && x.Message.DeletedAt == null, cancellationToken);

            mentions += mentionCount;

            if (m.IsMuted)
            {
                if (mentionCount > 0) conversations++;
                continue;
            }

            var count = await db.Messages.AsNoTracking()
                .CountAsync(x => x.ConversationId == m.ConversationId
                                 && x.SentAt > since
                                 && x.DeletedAt == null
                                 && x.AuthorUserId != userId, cancellationToken);

            messages += count;
            if (count > 0) conversations++;
        }

        return new UnreadSummaryDto(conversations, messages, mentions);
    }
}

// ---------------------------------------------------------------------------
// A thread
// ---------------------------------------------------------------------------

public sealed record GetConversationQuery(Guid ConversationId, int Take = 200)
    : IRequest<ConversationThreadDto>;

public sealed class GetConversationQueryHandler(IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<GetConversationQuery, ConversationThreadDto>
{
    public async Task<ConversationThreadDto> Handle(GetConversationQuery request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var conversation = await db.Conversations
                               .AsNoTracking()
                               .Include(c => c.Members)
                               .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken)
                           ?? throw new KeyNotFoundException("That conversation was not found.");

        MessagingAccess.EnsureCanRead(conversation, userId);

        var messages = await MessageProjection.ManyAsync(
            db, m => m.ConversationId == conversation.Id, userId,
            Math.Clamp(request.Take, 1, 500), cancellationToken);

        var participants = await ParticipantsAsync(db, conversation, cancellationToken);

        IReadOnlyList<ConversationBoardRefDto> relatedBoards =
            conversation.Kind == ConversationKind.Squad && conversation.SquadName is not null
            ? await db.Boards.AsNoTracking()
                .Where(b => !b.IsDeleted && b.SquadName.ToUpper() == conversation.SquadName.ToUpper())
                .OrderBy(b => b.Title)
                .Select(b => new ConversationBoardRefDto(b.Id, b.Title, b.Product, string.Empty))
                .ToListAsync(cancellationToken)
            : [];

        // The label is metadata, not a column, so it is applied after the query.
        relatedBoards = await ApplyStatusLabelsAsync(db, relatedBoards, cancellationToken);

        var counterpart = conversation.Kind == ConversationKind.Direct
            ? participants.FirstOrDefault(p => p.UserId != userId)
            : null;

        var title = conversation.Kind == ConversationKind.Direct
            ? counterpart?.DisplayName ?? "Direct message"
            : conversation.Title;

        var subtitle = conversation.Kind switch
        {
            ConversationKind.Board => await db.Boards.AsNoTracking()
                .Where(b => b.Id == conversation.BoardId)
                .Select(b => b.Product)
                .FirstOrDefaultAsync(cancellationToken),
            ConversationKind.Squad => relatedBoards.Count == 1
                ? "1 board"
                : $"{relatedBoards.Count} boards",
            _ => counterpart?.Headline
        };

        var me = conversation.Members.FirstOrDefault(m => m.UserId == userId);

        return new ConversationThreadDto(
            conversation.Id,
            conversation.Kind,
            MessagingAccess.KindLabel(conversation.Kind),
            title,
            subtitle,
            conversation.BoardId,
            conversation.SquadName,
            relatedBoards,
            participants,
            messages,
            me?.IsMuted ?? false,
            true);
    }

    /// <summary>
    /// Who is in this thread.
    ///
    /// For a channel that means the squad on the board (or boards), whether or not those
    /// people have an account — showing only the four who can sign in would misrepresent
    /// the squad as four people. Those without an account are marked, because knowing a
    /// mention will not reach someone is the point of showing them.
    /// </summary>
    private static async Task<IReadOnlyList<ConversationParticipantDto>> ParticipantsAsync(
        IAppDbContext db, Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Kind == ConversationKind.Direct)
        {
            var userIds = conversation.Members.Select(m => m.UserId).ToList();
            var readAt = conversation.Members.ToDictionary(m => m.UserId, m => m.LastReadAt);

            var accounts = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.DisplayName,
                    u.PersonId,
                    PersonName = db.People.Where(p => p.Id == u.PersonId).Select(p => p.FullName).FirstOrDefault(),
                    Headline = db.People.Where(p => p.Id == u.PersonId).Select(p => p.Headline).FirstOrDefault(),
                    Color = db.People.Where(p => p.Id == u.PersonId).Select(p => p.AvatarColorOverride).FirstOrDefault(),
                    Role = db.People.Where(p => p.Id == u.PersonId).Select(p => (Role?)p.DefaultRole).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return accounts.Select(a =>
            {
                var name = a.PersonName ?? a.DisplayName;
                return new ConversationParticipantDto(a.Id, a.PersonId, name,
                    Person.ComputeInitials(name),
                    a.Color ?? (a.Role is { } r ? RoleMetadata.Color(r) : MessageProjection.NeutralAvatarColor),
                    a.Headline, true, readAt.GetValueOrDefault(a.Id));
            }).ToList();
        }

        var people = await (conversation.Kind == ConversationKind.Board
                ? db.SquadMembers.AsNoTracking().Where(m => m.BoardId == conversation.BoardId)
                : db.SquadMembers.AsNoTracking().Where(m =>
                    db.Boards.Any(b => b.Id == m.BoardId && !b.IsDeleted
                                       && b.SquadName.ToUpper() == conversation.SquadName!.ToUpper())))
            .Select(m => new
            {
                m.PersonId,
                m.Person.FullName,
                m.Person.Headline,
                m.Person.AvatarColorOverride,
                m.Person.DefaultRole,
                UserId = db.Users.Where(u => u.PersonId == m.PersonId).Select(u => (Guid?)u.Id).FirstOrDefault()
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var readTimes = conversation.Members.ToDictionary(m => m.UserId, m => m.LastReadAt);

        return people
            .GroupBy(p => p.PersonId)
            .Select(g => g.First())
            .OrderBy(p => p.FullName)
            .Select(p => new ConversationParticipantDto(
                p.UserId ?? Guid.Empty,
                p.PersonId,
                p.FullName,
                Person.ComputeInitials(p.FullName),
                p.AvatarColorOverride ?? RoleMetadata.Color(p.DefaultRole),
                p.Headline,
                p.UserId is not null,
                p.UserId is { } id ? readTimes.GetValueOrDefault(id) : null))
            .ToList();
    }

    private static async Task<IReadOnlyList<ConversationBoardRefDto>> ApplyStatusLabelsAsync(
        IAppDbContext db, IReadOnlyList<ConversationBoardRefDto> boards,
        CancellationToken cancellationToken)
    {
        if (boards.Count == 0) return boards;

        var ids = boards.Select(b => b.Id).ToList();

        var statuses = await db.Boards.AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Status })
            .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);

        return boards
            .Select(b => b with
            {
                StatusLabel = statuses.TryGetValue(b.Id, out var s)
                    ? BoardStatusMetadata.Label(s)
                    : string.Empty
            })
            .ToList();
    }
}

// ---------------------------------------------------------------------------
// Starting something new
// ---------------------------------------------------------------------------

/// <summary>
/// Everywhere the caller could send a message: board channels, squad channels, and
/// colleagues with an account.
/// </summary>
public sealed record GetMessageTargetsQuery(string? Search = null)
    : IRequest<IReadOnlyList<MessageTargetDto>>;

public sealed class GetMessageTargetsQueryHandler(IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<GetMessageTargetsQuery, IReadOnlyList<MessageTargetDto>>
{
    public async Task<IReadOnlyList<MessageTargetDto>> Handle(
        GetMessageTargetsQuery request, CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);
        var term = request.Search?.Trim();

        var boards = await db.Boards.AsNoTracking()
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.Title)
            .Select(b => new { b.Id, b.Title, b.Product, b.SquadName })
            .ToListAsync(cancellationToken);

        var targets = boards
            .Select(b => new MessageTargetDto(MessagingAccess.BoardKindLabel, b.Title,
                b.Product, b.Id, null, null, null, null))
            .ToList();

        targets.AddRange(boards
            .GroupBy(b => Conversation.SquadScopeKey(b.SquadName))
            .Select(g => new
            {
                Name = g.First().SquadName,
                Count = g.Count()
            })
            .OrderBy(s => s.Name)
            .Select(s => new MessageTargetDto(MessagingAccess.SquadKindLabel, s.Name,
                s.Count == 1 ? "1 board" : $"{s.Count} boards", null, s.Name, null, null, null)));

        // Only accounts: a direct message to someone who cannot sign in would sit unread
        // for ever, so the picker does not offer it.
        var accounts = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != userId)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                PersonName = db.People.Where(p => p.Id == u.PersonId).Select(p => p.FullName).FirstOrDefault(),
                Headline = db.People.Where(p => p.Id == u.PersonId).Select(p => p.Headline).FirstOrDefault(),
                Color = db.People.Where(p => p.Id == u.PersonId).Select(p => p.AvatarColorOverride).FirstOrDefault(),
                Role = db.People.Where(p => p.Id == u.PersonId).Select(p => (Role?)p.DefaultRole).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        targets.AddRange(accounts
            .Select(a =>
            {
                var name = a.PersonName ?? a.DisplayName;
                return new MessageTargetDto(MessagingAccess.DirectKindLabel, name,
                    a.Headline ?? a.Email, null, null, a.Id,
                    Person.ComputeInitials(name),
                    a.Color ?? (a.Role is { } r ? RoleMetadata.Color(r) : MessageProjection.NeutralAvatarColor));
            })
            .OrderBy(t => t.Label));

        if (!string.IsNullOrWhiteSpace(term))
        {
            targets = targets
                .Where(t => t.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || (t.Sublabel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return targets;
    }
}
