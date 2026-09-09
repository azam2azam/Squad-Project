using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Telegram;

/// <summary>
/// Handles one inbound Telegram message, end to end: who sent it, which board they meant,
/// whether they are allowed to change it, and what to say back.
///
/// Two decisions shape this handler.
///
/// <b>It applies rather than suggests.</b> Jira and Smartsheet suggest, because a machine
/// inferring "you look at risk" from issue counts is a guess a person should confirm. A
/// Telegram message is not a guess — it is a person deliberately reporting their own
/// status, which is exactly what they would otherwise have typed into the board. Making
/// them then go and confirm it in the app would defeat the point of the integration.
///
/// <b>It authorises the sender explicitly, against the same rules as the editor.</b> Not
/// by impersonating them in the request pipeline, but by asking the board itself whether
/// this user may edit it — the same <see cref="Board.CanBeEditedBy"/> the API uses. A
/// Viewer with a linked Telegram account can read, and nothing more.
/// </summary>
public sealed record ProcessTelegramMessageCommand(
    TelegramInboundMessage Message,
    /// <summary>Parse, resolve and authorise, but write nothing. Used by the test button.</summary>
    bool DryRun = false) : IRequest<TelegramReply>;

/// <summary>What to send back, and what to write in the log.</summary>
public sealed record TelegramReply(
    string Text,
    TelegramOutcome Outcome,
    string Detail,
    Guid? BoardId = null,
    Guid? UserId = null)
{
    public bool ShouldReply => Text.Length > 0;
}

public sealed class ProcessTelegramMessageCommandHandler(
    IAppDbContext db,
    IBoardNotifier notifier)
    : IRequestHandler<ProcessTelegramMessageCommand, TelegramReply>
{
    public async Task<TelegramReply> Handle(
        ProcessTelegramMessageCommand request, CancellationToken cancellationToken)
    {
        var message = request.Message;

        var connection = await db.TelegramSettings
            .FirstOrDefaultAsync(s => s.Id == TelegramSettings.SingletonId, cancellationToken);

        // Chat id 0 is the simulator on the settings screen — Telegram never issues one, so
        // it cannot be spoofed by a real message, and the allow-list has nothing to say
        // about a conversation that did not happen in a chat.
        if (message.ChatId != 0 && connection is not null && !connection.AllowsChat(message.ChatId))
        {
            // Silence, not an error: a bot that argues in a group chat it was told to stay
            // out of is worse than one that says nothing.
            return new TelegramReply(string.Empty, TelegramOutcome.ChatNotAllowed,
                $"Chat {message.ChatId} is outside the allow-list.");
        }

        var parsed = TelegramMessageParser.Parse(message.Text);

        // /start carries the enrolment code, so it is the one thing an unlinked sender may do.
        if (parsed.IsCommand && parsed.Command is "/start" or "/link")
        {
            return await LinkAsync(message, parsed.CommandArgument, request.DryRun, cancellationToken);
        }

        var link = await db.TelegramLinks
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.TelegramUserId == message.SenderId, cancellationToken);

        if (link is null || !link.IsActive || link.User is null || !link.User.IsActive)
        {
            var reply = connection?.ReplyToUnknownSenders ?? true
                ? "I don't know who you are yet. Ask an administrator for an enrolment code, " +
                  "then send: /start YOURCODE"
                : string.Empty;

            return new TelegramReply(reply, TelegramOutcome.SenderNotLinked,
                $"No active link for Telegram user {message.SenderId}.");
        }

        link.Seen(message.SenderUsername, message.SenderName);

        if (parsed.IsCommand)
        {
            return await RunCommandAsync(parsed, link, cancellationToken);
        }

        if (!parsed.HasFields && parsed.BoardReference is null)
        {
            return new TelegramReply(
                "I couldn't find an update in that. Send it like this:\n\n"
                + TelegramMessageParser.Template,
                TelegramOutcome.NotUnderstood,
                "Nothing in the message parsed as a field.",
                UserId: link.UserId);
        }

        return await ApplyAsync(parsed, link, request.DryRun, cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Enrolment
    // -----------------------------------------------------------------------

    private async Task<TelegramReply> LinkAsync(TelegramInboundMessage message, string argument,
        bool dryRun, CancellationToken cancellationToken)
    {
        var code = argument.Trim().ToUpperInvariant();

        if (code.Length == 0)
        {
            return new TelegramReply(
                "Send /start followed by your enrolment code, for example: /start 4KDP2X8A",
                TelegramOutcome.Command, "Enrolment attempted with no code.");
        }

        var enrolment = await db.TelegramEnrolments
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Code == code, cancellationToken);

        if (enrolment is null || !enrolment.IsUsable(DateTimeOffset.UtcNow) || enrolment.User is null)
        {
            // One message for wrong, expired and already-used, so a wrong code cannot be
            // told apart from an expired one by somebody guessing.
            return new TelegramReply(
                "That code isn't valid — it may have expired or already been used. Ask for a new one.",
                TelegramOutcome.SenderNotLinked, $"Enrolment code {code} refused.");
        }

        if (!enrolment.User.IsActive)
        {
            return new TelegramReply("That account is not active.",
                TelegramOutcome.SenderNotLinked, "Enrolment code belongs to a deactivated user.");
        }

        if (dryRun)
        {
            return new TelegramReply($"Would link you to {enrolment.User.DisplayName}.",
                TelegramOutcome.Command, "Dry run: enrolment not written.");
        }

        var existing = await db.TelegramLinks
            .FirstOrDefaultAsync(l => l.TelegramUserId == message.SenderId, cancellationToken);

        if (existing is null)
        {
            db.TelegramLinks.Add(new TelegramLink(message.SenderId, message.SenderUsername,
                message.SenderName, enrolment.UserId));
        }
        else
        {
            // Re-enrolling moves the link rather than adding a second one: one Telegram
            // account is one person, and two links would make "who did this" ambiguous.
            existing.Restore(enrolment.UserId);
            existing.Seen(message.SenderUsername, message.SenderName);
        }

        enrolment.Redeem(message.SenderId);
        await db.SaveChangesAsync(cancellationToken);

        return new TelegramReply(
            $"Linked. You're posting as {enrolment.User.DisplayName}.\n\n"
            + "Send /boards to see what you can update, or /help for the template.",
            TelegramOutcome.Command, $"Linked to {enrolment.User.DisplayName}.",
            UserId: enrolment.UserId);
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    private async Task<TelegramReply> RunCommandAsync(ParsedTelegramMessage parsed,
        TelegramLink link, CancellationToken cancellationToken)
    {
        switch (parsed.Command)
        {
            case "/help":
                return Command(
                    "Send an update like this:\n\n" + TelegramMessageParser.Template
                    + "\n\nOnly the lines you include change. Commands: /boards, /status CODE, /whoami.",
                    "Sent the template.", link);

            case "/whoami":
                return Command(
                    $"You're posting as {link.User!.DisplayName} ({link.User.Role}).",
                    "Reported identity.", link);

            case "/boards":
            {
                var boards = await VisibleBoardsAsync(link, cancellationToken);

                if (boards.Count == 0)
                {
                    return Command("You have no boards you can update.", "No editable boards.", link);
                }

                var lines = boards
                    .Take(30)
                    .Select(b => $"{b.Code ?? "(no code)"} — {b.Title} · {BoardStatusMetadata.Label(b.Status)} {b.ProgressPercent}%");

                var more = boards.Count > 30 ? $"\n…and {boards.Count - 30} more." : string.Empty;

                return Command(string.Join('\n', lines) + more,
                    $"Listed {boards.Count} board(s).", link);
            }

            case "/status":
            {
                var boards = await VisibleBoardsAsync(link, cancellationToken);
                var match = TelegramBoardResolver.Resolve(boards, parsed.CommandArgument);

                if (match.Board is null)
                {
                    return Command(match.Message, "Board not resolved for /status.", link);
                }

                var board = match.Board;

                return Command(
                    $"{board.Code ?? board.Title}\n"
                    + $"status: {BoardStatusMetadata.Label(board.Status)}\n"
                    + $"progress: {board.ProgressPercent}%\n"
                    + $"sprint: {board.Sprint ?? "—"}\n"
                    + $"risk: {board.RiskLevel}\n"
                    + $"blocker: {board.BlockerNote ?? "—"}",
                    $"Reported status of {board.Title}.", link, board.Id);
            }

            default:
                return Command(
                    "I don't know that command. Try /help, /boards, /status CODE or /whoami.",
                    $"Unknown command {parsed.Command}.", link);
        }
    }

    private static TelegramReply Command(string text, string detail, TelegramLink link,
        Guid? boardId = null) =>
        new(text, TelegramOutcome.Command, detail, boardId, link.UserId);

    // -----------------------------------------------------------------------
    // Applying an update
    // -----------------------------------------------------------------------

    private async Task<TelegramReply> ApplyAsync(ParsedTelegramMessage parsed, TelegramLink link,
        bool dryRun, CancellationToken cancellationToken)
    {
        var user = link.User!;
        var boards = await VisibleBoardsAsync(link, cancellationToken);
        var match = TelegramBoardResolver.Resolve(boards, parsed.BoardReference);

        if (match.Board is null)
        {
            return new TelegramReply(match.Message, TelegramOutcome.BoardNotResolved,
                match.Message, UserId: link.UserId);
        }

        // Reloaded with its navigations so the SignalR payload is the same shape the editor
        // sends. EF returns the instance already tracked, so this is not a second board.
        var board = await db.Boards
            .Include(b => b.Category)
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .FirstAsync(b => b.Id == match.Board.Id, cancellationToken);

        // The same rule the editor enforces, asked of the board itself rather than
        // re-implemented here.
        if (!board.CanBeEditedBy(user.Id, user.Role))
        {
            return new TelegramReply(
                user.Role == UserRole.Viewer
                    ? "You have read-only access, so I can't change a board for you."
                    : $"You don't own {board.Code ?? board.Title}, so I can't change it. "
                      + "Ask an administrator to reassign it.",
                TelegramOutcome.NotPermitted,
                $"{user.DisplayName} may not edit {board.Title}.",
                board.Id, link.UserId);
        }

        if (!parsed.HasFields)
        {
            return new TelegramReply(
                $"I matched {board.Code ?? board.Title} but there was nothing to change. "
                + "Add a line like \"status: At Risk\" or \"progress: 65\".",
                TelegramOutcome.NotUnderstood, "Board resolved but no fields given.",
                board.Id, link.UserId);
        }

        // A free "note:" becomes the risk note when a risk is being raised, and otherwise
        // the blocker note when the board is being blocked — the two places a reader looks
        // for the reason. Anything else, and it would silently go nowhere.
        var riskNote = parsed.RiskNote;
        var blockerNote = parsed.BlockerNote;

        if (parsed.Note is { } note)
        {
            var raisingRisk = parsed.RiskLevel is not null and not RiskLevel.None
                              || parsed.Status == BoardStatus.AtRisk
                              || board.RiskLevel >= RiskLevel.Medium;

            if (parsed.Status == BoardStatus.Blocked && blockerNote is null) blockerNote = note;
            else if (raisingRisk && riskNote is null) riskNote = note;
            else blockerNote ??= note;
        }

        // What the board would become. Worked out before anything is written, because a dry
        // run must leave the tracked entity untouched — a mutation "not saved" is still
        // sitting in the change tracker waiting for somebody else's SaveChanges.
        var target = new
        {
            Status = parsed.Status ?? board.Status,
            Progress = parsed.ProgressPercent ?? board.ProgressPercent,
            Sprint = parsed.Sprint ?? board.Sprint,
            Blocker = blockerNote ?? board.BlockerNote,
            Risk = parsed.RiskLevel ?? board.RiskLevel,
            RiskNote = riskNote ?? board.RiskNote
        };

        var changes = new List<string>();
        var entries = new List<BoardAuditEntry>();

        if (target.Status != board.Status)
        {
            changes.Add($"status → {BoardStatusMetadata.Label(target.Status)}");
            entries.Add(Entry(board.Id, "Status", BoardStatusMetadata.Label(board.Status),
                BoardStatusMetadata.Label(target.Status), user.DisplayName));
        }

        if (target.Progress != board.ProgressPercent)
        {
            changes.Add($"progress → {target.Progress}%");
            entries.Add(Entry(board.Id, "Progress", $"{board.ProgressPercent}%",
                $"{target.Progress}%", user.DisplayName));
        }

        if (target.Sprint != board.Sprint)
        {
            changes.Add($"sprint → {target.Sprint}");
            entries.Add(Entry(board.Id, "Sprint", board.Sprint, target.Sprint, user.DisplayName));
        }

        if (target.Blocker != board.BlockerNote)
        {
            changes.Add("blocker note updated");
            entries.Add(Entry(board.Id, "Blocker note", board.BlockerNote, target.Blocker,
                user.DisplayName));
        }

        if (target.Risk != board.RiskLevel)
        {
            changes.Add($"risk → {target.Risk}");
            entries.Add(Entry(board.Id, "Risk", board.RiskLevel.ToString(), target.Risk.ToString(),
                user.DisplayName));
        }

        if (target.RiskNote != board.RiskNote)
        {
            changes.Add("risk note updated");
            entries.Add(Entry(board.Id, "Risk note", board.RiskNote, target.RiskNote,
                user.DisplayName));
        }

        if (changes.Count == 0)
        {
            return new TelegramReply(
                $"{board.Code ?? board.Title} already said that — nothing changed.",
                TelegramOutcome.NoChange, "Every value already matched.", board.Id, link.UserId);
        }

        if (dryRun)
        {
            return new TelegramReply(
                $"Would update {board.Code ?? board.Title}: {string.Join(", ", changes)}.",
                TelegramOutcome.Applied, $"Dry run: {string.Join(", ", changes)}.",
                board.Id, link.UserId);
        }

        board.UpdateMeta(
            board.Title,
            board.Product,
            board.SquadName,
            target.Sprint,
            target.Status,
            target.Progress,
            target.Blocker,
            board.Velocity,
            board.TargetDate,
            board.JiraProjectKey,
            board.JiraBoardId,
            target.Risk,
            target.RiskNote);

        db.BoardAuditEntries.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);

        // Anyone with that board open sees it move, exactly as if it had been edited in
        // the app — which, as far as the board is concerned, it was.
        await notifier.BoardUpdatedAsync(board.Id, BoardDetailDto.From(board), cancellationToken);

        var warning = parsed.Unrecognised.Count > 0
            ? $"\n\nI ignored: {string.Join("; ", parsed.Unrecognised.Take(3))}"
            : string.Empty;

        return new TelegramReply(
            $"Updated {board.Code ?? board.Title}: {string.Join(", ", changes)}." + warning,
            TelegramOutcome.Applied, string.Join(", ", changes), board.Id, link.UserId);
    }

    /// <summary>
    /// Built, not added: the entries are held until the write is certain, so a dry run
    /// leaves nothing behind in the change tracker.
    /// </summary>
    private static BoardAuditEntry Entry(Guid boardId, string field, string? oldValue,
        string? newValue, string by) =>
        new(boardId, field, oldValue, newValue, by, source: "Telegram");

    /// <summary>
    /// The boards this person could plausibly mean. An admin sees everything; a Product
    /// Owner sees their own, because resolving to a board they cannot edit only produces a
    /// refusal one step later.
    /// </summary>
    private async Task<IReadOnlyList<Board>> VisibleBoardsAsync(TelegramLink link,
        CancellationToken cancellationToken)
    {
        var user = link.User!;

        var query = db.Boards.AsQueryable();

        if (user.Role == UserRole.ProductOwner)
        {
            query = query.Where(b => b.OwnerId == user.Id);
        }

        return await query.OrderBy(b => b.OrderIndex).ToListAsync(cancellationToken);
    }
}
