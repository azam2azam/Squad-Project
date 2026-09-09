using System.Security.Cryptography;
using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Telegram;

// ---------------------------------------------------------------------------
// Enrolment
// ---------------------------------------------------------------------------

/// <summary>
/// Issues the one-time code somebody sends to the bot as <c>/start CODE</c>.
///
/// An admin issues codes for other people; anyone may issue one for themselves, because
/// needing an administrator present to connect your own phone is the kind of friction that
/// quietly kills an integration.
/// </summary>
public sealed record CreateTelegramEnrolmentCommand(Guid? UserId = null)
    : IRequest<TelegramEnrolmentDto>;

public sealed record TelegramEnrolmentDto(
    string Code, string UserDisplayName, DateTimeOffset ExpiresAt, string Instructions);

public sealed class CreateTelegramEnrolmentCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, IBoardAuthorizer authorizer,
    ITelegramSettingsService settings)
    : IRequestHandler<CreateTelegramEnrolmentCommand, TelegramEnrolmentDto>
{
    /// <summary>
    /// Short enough to type on a phone, long enough that guessing inside the window is
    /// hopeless — and it lives for thirty minutes, not forever.
    /// </summary>
    private static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(30);

    public async Task<TelegramEnrolmentDto> Handle(
        CreateTelegramEnrolmentCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            throw new UnauthorizedException("You must be signed in to do that.");
        }

        var targetId = request.UserId ?? callerId;

        // Issuing a code for somebody else is issuing them an identity, so that is an
        // administrator's job. Issuing your own is not.
        if (targetId != callerId) authorizer.EnsureIsAdmin();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == targetId, cancellationToken)
                   ?? throw new KeyNotFoundException("That user was not found.");

        if (!user.IsActive)
        {
            throw new DomainException("That account is not active.");
        }

        var caller = await db.Users.FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken);

        // Any code still alive for this person is retired: two live codes for one account
        // is one more than anybody needs, and the older one is the one that leaks.
        var outstanding = await db.TelegramEnrolments
            .Where(e => e.UserId == targetId && e.RedeemedAt == null
                        && e.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        db.TelegramEnrolments.RemoveRange(outstanding);

        var code = await UniqueCodeAsync(db, cancellationToken);

        db.TelegramEnrolments.Add(new TelegramEnrolment(code, targetId,
            caller?.DisplayName ?? "system", ValidFor));

        await db.SaveChangesAsync(cancellationToken);

        var connection = await settings.GetAsync(cancellationToken);
        var bot = connection.BotUsername is { } username ? $"@{username}" : "the bot";

        return new TelegramEnrolmentDto(
            code,
            user.DisplayName,
            DateTimeOffset.UtcNow.Add(ValidFor),
            $"Open Telegram, message {bot}, and send:  /start {code}");
    }

    private static async Task<string> UniqueCodeAsync(IAppDbContext db,
        CancellationToken cancellationToken)
    {
        // Ambiguous characters are left out: a code read off a screen and typed on a phone
        // should not turn on telling O from 0.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, TelegramEnrolment.CodeLength)
                .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])
                .ToArray());

            if (!await db.TelegramEnrolments.AnyAsync(e => e.Code == code, cancellationToken))
            {
                return code;
            }
        }

        throw new DomainException("Could not generate an enrolment code. Try again.");
    }
}

// ---------------------------------------------------------------------------
// Links
// ---------------------------------------------------------------------------

public sealed record GetTelegramLinksQuery : IRequest<IReadOnlyList<TelegramLinkDto>>;

public sealed record TelegramLinkDto(
    Guid Id,
    long TelegramUserId,
    string? TelegramUsername,
    string DisplayName,
    Guid UserId,
    string UserDisplayName,
    string UserRole,
    bool IsActive,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastSeenAt);

public sealed class GetTelegramLinksQueryHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<GetTelegramLinksQuery, IReadOnlyList<TelegramLinkDto>>
{
    public async Task<IReadOnlyList<TelegramLinkDto>> Handle(
        GetTelegramLinksQuery request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        return await db.TelegramLinks
            .Include(l => l.User)
            .OrderByDescending(l => l.IsActive)
            .ThenBy(l => l.DisplayName)
            .Select(l => new TelegramLinkDto(
                l.Id,
                l.TelegramUserId,
                l.TelegramUsername,
                l.DisplayName,
                l.UserId,
                l.User!.DisplayName,
                l.User.Role.ToString(),
                l.IsActive,
                l.LinkedAt,
                l.LastSeenAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Cuts off one Telegram account. The person's application login is untouched.</summary>
public sealed record RevokeTelegramLinkCommand(Guid Id) : IRequest;

public sealed class RevokeTelegramLinkCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<RevokeTelegramLinkCommand>
{
    public async Task Handle(RevokeTelegramLinkCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var link = await db.TelegramLinks.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That link was not found.");

        link.Revoke();
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Message log
// ---------------------------------------------------------------------------

public sealed record GetTelegramMessagesQuery(int Take = 40)
    : IRequest<IReadOnlyList<TelegramMessageDto>>;

public sealed record TelegramMessageDto(
    Guid Id,
    DateTimeOffset ReceivedAt,
    string SenderName,
    string Text,
    string Outcome,
    string OutcomeLabel,
    bool Succeeded,
    string? Detail,
    Guid? BoardId,
    string? BoardTitle);

public sealed class GetTelegramMessagesQueryHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<GetTelegramMessagesQuery, IReadOnlyList<TelegramMessageDto>>
{
    public async Task<IReadOnlyList<TelegramMessageDto>> Handle(
        GetTelegramMessagesQuery request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var take = Math.Clamp(request.Take, 1, 200);

        var messages = await db.TelegramMessages
            .OrderByDescending(m => m.ReceivedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        // Titles are looked up rather than joined: the log deliberately has no foreign key
        // to Board, so a board can be deleted without taking its history of updates.
        var boardIds = messages.Where(m => m.BoardId.HasValue).Select(m => m.BoardId!.Value).ToList();

        var titles = await db.Boards
            .IgnoreQueryFilters()
            .Where(b => boardIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title })
            .ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);

        return messages.Select(m => new TelegramMessageDto(
            m.Id,
            m.ReceivedAt,
            m.SenderName,
            m.Text,
            m.Outcome.ToString(),
            TelegramOutcomeText.Label(m.Outcome),
            m.Outcome is TelegramOutcome.Applied or TelegramOutcome.Command or TelegramOutcome.NoChange,
            m.Detail,
            m.BoardId,
            m.BoardId is { } id && titles.TryGetValue(id, out var title) ? title : null))
            .ToList();
    }
}

/// <summary>Reader-facing wording for an outcome. Kept out of the enum, which is storage.</summary>
public static class TelegramOutcomeText
{
    public static string Label(TelegramOutcome outcome) => outcome switch
    {
        TelegramOutcome.Received => "Received",
        TelegramOutcome.Applied => "Applied",
        TelegramOutcome.NoChange => "No change",
        TelegramOutcome.Command => "Command",
        TelegramOutcome.SenderNotLinked => "Sender not linked",
        TelegramOutcome.NotPermitted => "Not permitted",
        TelegramOutcome.BoardNotResolved => "Board not found",
        TelegramOutcome.NotUnderstood => "Not understood",
        TelegramOutcome.ChatNotAllowed => "Chat not allowed",
        _ => "Failed"
    };
}

// ---------------------------------------------------------------------------
// Board codes
// ---------------------------------------------------------------------------

/// <summary>
/// Gives every board a code, leaving existing ones alone.
///
/// Run once after switching Telegram on. Codes are derived from titles and de-duplicated
/// with a numeric suffix, which is a better starting point than a blank column — anyone can
/// then rename the two or three that read badly.
/// </summary>
public sealed record AssignBoardCodesCommand : IRequest<IReadOnlyList<BoardCodeDto>>;

public sealed record BoardCodeDto(Guid Id, string Title, string? Code, bool Assigned);

public sealed class AssignBoardCodesCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<AssignBoardCodesCommand, IReadOnlyList<BoardCodeDto>>
{
    public async Task<IReadOnlyList<BoardCodeDto>> Handle(
        AssignBoardCodesCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var boards = await db.Boards.OrderBy(b => b.OrderIndex).ToListAsync(cancellationToken);

        var taken = boards
            .Where(b => b.Code is not null)
            .Select(b => b.Code!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<BoardCodeDto>();

        foreach (var board in boards)
        {
            if (board.Code is not null)
            {
                results.Add(new BoardCodeDto(board.Id, board.Title, board.Code, Assigned: false));
                continue;
            }

            var code = Unique(Board.SuggestCode(board.Title), taken);
            board.AssignCode(code);
            taken.Add(code);

            results.Add(new BoardCodeDto(board.Id, board.Title, code, Assigned: true));
        }

        await db.SaveChangesAsync(cancellationToken);

        return results;
    }

    private static string Unique(string candidate, HashSet<string> taken)
    {
        if (!taken.Contains(candidate)) return candidate;

        for (var suffix = 2; suffix < 100; suffix++)
        {
            // Trimmed from the front so the suffix always fits inside the 12-character cap.
            var trimmed = candidate[..Math.Min(candidate.Length, 12 - suffix.ToString().Length)];
            var next = trimmed + suffix;

            if (!taken.Contains(next)) return next;
        }

        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
