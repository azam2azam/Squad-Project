using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Messaging;

/// <summary>Writes a message into a conversation.</summary>
public sealed record PostMessageCommand(Guid ConversationId, string Body, Guid? ReplyToMessageId = null)
    : IRequest<MessageDto>;

public sealed class PostMessageCommandValidator : AbstractValidator<PostMessageCommand>
{
    public PostMessageCommandValidator()
    {
        RuleFor(c => c.ConversationId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class PostMessageCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, IMessageNotifier notifier)
    : IRequestHandler<PostMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(PostMessageCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);
        var conversation = await MessagingAccess.LoadAsync(db, request.ConversationId, cancellationToken);

        MessagingAccess.EnsureCanRead(conversation, userId);

        var author = await db.Users
                         .AsNoTracking()
                         .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                     ?? throw new UnauthorizedException("Your account was not found.");

        if (request.ReplyToMessageId is { } replyTo)
        {
            var target = await db.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == replyTo, cancellationToken);

            // A reply that points into a different thread would render as an orphan
            // quotation, so it is refused rather than silently flattened.
            if (target is null || target.ConversationId != conversation.Id)
            {
                throw new DomainException("You can only reply to a message in this conversation.");
            }
        }

        var message = new Message(conversation.Id, userId, request.Body, request.ReplyToMessageId);

        await ResolveMentionsAsync(db, message, cancellationToken);

        // Added to the set directly rather than through a navigation on Conversation:
        // this entity assigns its own id in the constructor, so a child reached through a
        // tracked parent is marked Modified and EF updates a row that does not exist.
        db.Messages.Add(message);

        // Posting is reading: nobody should return to a thread they just wrote in and be
        // told they have one unread message.
        var membership = MessagingAccess.Join(db, conversation, userId);
        membership.MarkReadAt(message.SentAt);

        conversation.RecordMessage(message.Body, author.DisplayName, message.SentAt);

        await db.SaveChangesAsync(cancellationToken);

        var dto = await MessageProjection.OneAsync(db, message.Id, userId, cancellationToken);

        await notifier.MessagePostedAsync(conversation.Id, dto, cancellationToken);

        return dto;
    }

    /// <summary>
    /// Turns "@Sara Al-Otaibi" into a link to an account.
    ///
    /// Longest match wins, because a name is several words and the parser cannot know
    /// where it stops: given "@Sara Al-Otaibi can you", both "Sara" and "Sara Al-Otaibi"
    /// are candidates and only the longer one is the person who gets notified.
    /// </summary>
    private static async Task ResolveMentionsAsync(IAppDbContext db, Message message,
        CancellationToken cancellationToken)
    {
        var occurrences = Message.ParseMentionCandidates(message.Body);
        if (occurrences.Count == 0) return;

        var accounts = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                PersonName = db.People
                    .Where(p => p.Id == u.PersonId)
                    .Select(p => p.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        foreach (var readings in occurrences)
        {
            // Longest first, and stop at the first hit: "@Sara Al-Otaibi" is Sara
            // Al-Otaibi, not whoever else on the roster happens to be called Sara.
            foreach (var candidate in readings)
            {
                var match = accounts.FirstOrDefault(a =>
                    string.Equals(a.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)
                    || (a.PersonName is not null
                        && string.Equals(a.PersonName, candidate, StringComparison.OrdinalIgnoreCase)));

                if (match is null) continue;

                message.AddMention(match.Id);
                break;
            }
        }
    }
}

/// <summary>Rewrites a message. The author only — not an administrator.</summary>
public sealed record EditMessageCommand(Guid MessageId, string Body) : IRequest<MessageDto>;

public sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator()
    {
        RuleFor(c => c.MessageId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class EditMessageCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, IMessageNotifier notifier)
    : IRequestHandler<EditMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(EditMessageCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var message = await db.Messages
                          .Include(m => m.Mentions)
                          .FirstOrDefaultAsync(m => m.Id == request.MessageId, cancellationToken)
                      ?? throw new KeyNotFoundException("That message was not found.");

        // Deliberately not an admin power. Editing somebody else's words under their name
        // is not moderation, it is forgery — an admin who needs something gone deletes it,
        // which leaves a visible tombstone.
        if (message.AuthorUserId != userId)
        {
            throw new ForbiddenException("You can only edit your own messages.");
        }

        message.Edit(request.Body);
        await db.SaveChangesAsync(cancellationToken);

        var dto = await MessageProjection.OneAsync(db, message.Id, userId, cancellationToken);
        await notifier.MessageChangedAsync(message.ConversationId, dto, cancellationToken);

        return dto;
    }
}

/// <summary>
/// Withdraws a message. The author, or an administrator moderating the channel.
/// </summary>
public sealed record DeleteMessageCommand(Guid MessageId) : IRequest<MessageDto>;

public sealed class DeleteMessageCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, IMessageNotifier notifier)
    : IRequestHandler<DeleteMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(DeleteMessageCommand request,
        CancellationToken cancellationToken)
    {
        var userId = MessagingAccess.RequireUserId(currentUser);

        var message = await db.Messages
                          .Include(m => m.Mentions)
                          .FirstOrDefaultAsync(m => m.Id == request.MessageId, cancellationToken)
                      ?? throw new KeyNotFoundException("That message was not found.");

        var conversation = await MessagingAccess.LoadAsync(db, message.ConversationId, cancellationToken);

        // An admin may moderate a channel, but a direct thread stays between its two
        // people — including for deletion.
        var isAdmin = currentUser.Role == Domain.Enums.UserRole.Admin
                      && conversation.Kind != Domain.Enums.ConversationKind.Direct;

        if (message.AuthorUserId != userId && !isAdmin)
        {
            throw new ForbiddenException("You can only delete your own messages.");
        }

        message.Delete();
        await db.SaveChangesAsync(cancellationToken);

        var dto = await MessageProjection.OneAsync(db, message.Id, userId, cancellationToken);
        await notifier.MessageChangedAsync(message.ConversationId, dto, cancellationToken);

        return dto;
    }
}
