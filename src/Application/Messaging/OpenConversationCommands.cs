using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Messaging;

/// <summary>
/// Resolves the conversation for a board, creating it the first time anyone asks.
///
/// "Open" rather than "create": the caller does not know or care whether the channel
/// already exists, and making that its problem would mean every entry point in the UI
/// handling a race between two people clicking Discuss at the same moment.
/// </summary>
public sealed record OpenBoardConversationCommand(Guid BoardId) : IRequest<Guid>;

public sealed class OpenBoardConversationCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<OpenBoardConversationCommand, Guid>
{
    public async Task<Guid> Handle(OpenBoardConversationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var board = await db.Boards
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == request.BoardId && !b.IsDeleted,
                            cancellationToken)
                    ?? throw new KeyNotFoundException("That board was not found.");

        var scopeKey = Conversation.BoardScopeKey(board.Id);

        var conversation = await db.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.ScopeKey == scopeKey, cancellationToken);

        if (conversation is null)
        {
            conversation = Conversation.ForBoard(board.Id, board.Title);
            db.Conversations.Add(conversation);
        }
        else
        {
            // The board may have been retitled since the channel was opened.
            conversation.Rename(board.Title);
        }

        MessagingAccess.Join(db, conversation, userId);
        await db.SaveChangesAsync(cancellationToken);

        return conversation.Id;
    }
}

/// <summary>
/// Resolves the conversation for a squad — the one that spans every board that squad runs.
/// </summary>
public sealed record OpenSquadConversationCommand(string SquadName) : IRequest<Guid>;

public sealed class OpenSquadConversationCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<OpenSquadConversationCommand, Guid>
{
    public async Task<Guid> Handle(OpenSquadConversationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var squadName = (request.SquadName ?? string.Empty).Trim();

        if (squadName.Length == 0)
        {
            throw new KeyNotFoundException("That squad was not found.");
        }

        // The squad has to actually exist on a board. Otherwise a typo in a URL silently
        // creates a channel for a squad nobody has heard of, and the inbox fills with
        // ghosts. Resolving also gives back the spelling the boards use, so the channel
        // is titled the way the squad is written everywhere else.
        var canonical = await MessagingAccess.ResolveSquadNameAsync(db, squadName, cancellationToken)
            ?? throw new KeyNotFoundException($"No board is run by a squad called \"{squadName}\".");

        var scopeKey = Conversation.SquadScopeKey(canonical);

        var conversation = await db.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.ScopeKey == scopeKey, cancellationToken);

        if (conversation is null)
        {
            conversation = Conversation.ForSquad(canonical);
            db.Conversations.Add(conversation);
        }

        MessagingAccess.Join(db, conversation, userId);
        await db.SaveChangesAsync(cancellationToken);

        return conversation.Id;
    }
}

/// <summary>
/// Resolves the two-person thread between the caller and somebody else.
///
/// Keyed on an order-independent pair id, so it does not matter which of the two opened
/// it first — there is one thread between two people and both of them find it.
/// </summary>
public sealed record OpenDirectConversationCommand(Guid OtherUserId) : IRequest<Guid>;

public sealed class OpenDirectConversationCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<OpenDirectConversationCommand, Guid>
{
    public async Task<Guid> Handle(OpenDirectConversationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        if (request.OtherUserId == userId)
        {
            throw new ForbiddenException("You cannot start a conversation with yourself.");
        }

        var other = await db.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == request.OtherUserId, cancellationToken)
                    ?? throw new KeyNotFoundException("That person was not found.");

        if (!other.IsActive)
        {
            throw new ForbiddenException($"{other.DisplayName}'s account is deactivated.");
        }

        var scopeKey = Conversation.DirectScopeKey(userId, other.Id);

        var conversation = await db.Conversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.ScopeKey == scopeKey, cancellationToken);

        if (conversation is null)
        {
            conversation = Conversation.ForDirect(userId, other.Id);
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);
        }

        return conversation.Id;
    }
}

/// <summary>Leaves a channel, or mutes any conversation.</summary>
public sealed record SetConversationMutedCommand(Guid ConversationId, bool Muted) : IRequest;

public sealed class SetConversationMutedCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<SetConversationMutedCommand>
{
    public async Task Handle(SetConversationMutedCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);
        var conversation = await MessagingAccess.LoadAsync(db, request.ConversationId, cancellationToken);

        MessagingAccess.EnsureCanRead(conversation, userId);

        MessagingAccess.Join(db, conversation, userId).SetMuted(request.Muted);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record LeaveConversationCommand(Guid ConversationId) : IRequest;

public sealed class LeaveConversationCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<LeaveConversationCommand>
{
    public async Task Handle(LeaveConversationCommand request, CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);
        var conversation = await MessagingAccess.LoadAsync(db, request.ConversationId, cancellationToken);

        MessagingAccess.EnsureCanRead(conversation, userId);

        conversation.Leave(userId);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Marks a thread read up to now.
///
/// Read state is per person and advances only forwards, so two tabs cannot un-read
/// something between them.
/// </summary>
public sealed record MarkConversationReadCommand(Guid ConversationId) : IRequest;

public sealed class MarkConversationReadCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<MarkConversationReadCommand>
{
    public async Task Handle(MarkConversationReadCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);
        var conversation = await MessagingAccess.LoadAsync(db, request.ConversationId, cancellationToken);

        MessagingAccess.EnsureCanRead(conversation, userId);

        MessagingAccess.Join(db, conversation, userId).MarkReadAt(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}
