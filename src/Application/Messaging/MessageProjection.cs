using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Messaging;

/// <summary>
/// Turns message rows into the shape the client renders.
///
/// One place, because a message is returned from five different paths — the thread, a
/// post, an edit, a delete and the live push — and a message that looks different
/// depending on which of those produced it is a bug waiting to be reported as "the avatar
/// changes when I edit".
/// </summary>
public static class MessageProjection
{
    public static async Task<MessageDto> OneAsync(IAppDbContext db, Guid messageId,
        Guid readerId, CancellationToken cancellationToken)
    {
        var dtos = await ManyAsync(db, m => m.Id == messageId, readerId, null, cancellationToken);

        return dtos.SingleOrDefault()
               ?? throw new KeyNotFoundException("That message was not found.");
    }

    public static async Task<IReadOnlyList<MessageDto>> ManyAsync(
        IAppDbContext db,
        System.Linq.Expressions.Expression<Func<Message, bool>> predicate,
        Guid readerId,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = db.Messages.AsNoTracking().Where(predicate);

        // Newest first for the take, so a long thread loads its tail rather than its head.
        if (take is { } limit)
        {
            query = query.OrderByDescending(m => m.SentAt).Take(limit);
        }

        var rows = await query
            .Select(m => new
            {
                m.Id,
                m.AuthorUserId,
                AuthorName = m.Author.DisplayName,
                AuthorPersonId = m.Author.PersonId,
                PersonName = db.People.Where(p => p.Id == m.Author.PersonId)
                    .Select(p => p.FullName).FirstOrDefault(),
                PersonRole = db.People.Where(p => p.Id == m.Author.PersonId)
                    .Select(p => (Role?)p.DefaultRole).FirstOrDefault(),
                PersonColor = db.People.Where(p => p.Id == m.Author.PersonId)
                    .Select(p => p.AvatarColorOverride).FirstOrDefault(),
                m.Body,
                m.SentAt,
                m.EditedAt,
                m.DeletedAt,
                m.ReplyToMessageId,
                ReplyToAuthor = m.ReplyTo != null ? m.ReplyTo.Author.DisplayName : null,
                ReplyToBody = m.ReplyTo != null ? m.ReplyTo.Body : null,
                ReplyToDeleted = m.ReplyTo != null && m.ReplyTo.DeletedAt != null,
                MentionsMe = db.MessageMentions
                    .Any(x => x.MessageId == m.Id && x.MentionedUserId == readerId)
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.SentAt)
            .Select(r =>
            {
                // The roster name wins over the account's display name: the directory is
                // where people curate how they are known, and an account created by an
                // administrator often says "a.alotaibi".
                var name = r.PersonName ?? r.AuthorName;

                return new MessageDto(
                    r.Id,
                    r.AuthorUserId,
                    name,
                    Person.ComputeInitials(name),
                    r.PersonColor ?? (r.PersonRole is { } role
                        ? RoleMetadata.Color(role)
                        : NeutralAvatarColor),
                    r.AuthorPersonId,
                    r.DeletedAt is null ? r.Body : string.Empty,
                    r.SentAt,
                    r.EditedAt,
                    r.DeletedAt is not null,
                    r.AuthorUserId == readerId,
                    r.MentionsMe,
                    r.ReplyToMessageId,
                    r.ReplyToAuthor,
                    Excerpt(r.ReplyToBody, r.ReplyToDeleted));
            })
            .ToList();
    }

    /// <summary>
    /// For an account with no roster link — an administrator, usually. Grey rather than a
    /// role colour, because inventing one would imply a role they have not been given.
    /// </summary>
    public const string NeutralAvatarColor = "#64748b";

    private static string? Excerpt(string? body, bool deleted)
    {
        if (deleted) return "Message withdrawn";
        if (string.IsNullOrWhiteSpace(body)) return null;

        var flat = string.Join(' ', body.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length <= 90 ? flat : flat[..89] + "…";
    }
}
