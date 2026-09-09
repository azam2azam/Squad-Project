using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Telegram;

/// <summary>
/// One polling pass: read whatever Telegram has, act on each message, answer it, and move
/// the offset forward.
///
/// The order matters and is deliberate. A message is logged, then processed, then
/// acknowledged. Telegram redelivers anything unacknowledged, so a crash mid-pass means the
/// message comes back — and the unique index on the update id is what stops it being
/// applied a second time. Losing an update is recoverable by resending; applying one twice
/// silently is not.
/// </summary>
public sealed record PollTelegramCommand(int TimeoutSeconds = 20) : IRequest<TelegramPollReport>;

public sealed record TelegramPollReport(int Read, int Applied, int Refused, string Message);

public sealed class PollTelegramCommandHandler(
    IAppDbContext db,
    ITelegramClient client,
    ITelegramSettingsService settings,
    ISender sender)
    : IRequestHandler<PollTelegramCommand, TelegramPollReport>
{
    public async Task<TelegramPollReport> Handle(
        PollTelegramCommand request, CancellationToken cancellationToken)
    {
        var credentials = await settings.GetCredentialsAsync(cancellationToken);

        if (credentials is null)
        {
            return new TelegramPollReport(0, 0, 0, "Telegram is not connected.");
        }

        // Telegram treats the offset as "everything below this is done with", so it is the
        // last id seen plus one.
        var updates = await client.GetUpdatesAsync(credentials.LastUpdateId + 1,
            request.TimeoutSeconds, cancellationToken);

        if (updates.Count == 0)
        {
            return new TelegramPollReport(0, 0, 0, "Nothing new.");
        }

        var applied = 0;
        var refused = 0;
        var highest = credentials.LastUpdateId;

        foreach (var update in updates)
        {
            highest = Math.Max(highest, update.UpdateId);

            // A redelivery of something already handled. Skipped rather than reprocessed —
            // this is the guard that makes an interrupted pass safe.
            if (await db.TelegramMessages.AnyAsync(m => m.UpdateId == update.UpdateId,
                    cancellationToken))
            {
                continue;
            }

            var log = new TelegramMessage(update.UpdateId, update.ChatId, update.SenderId,
                update.SenderName, update.Text);

            db.TelegramMessages.Add(log);

            TelegramReply reply;

            try
            {
                reply = await sender.Send(new ProcessTelegramMessageCommand(update), cancellationToken);
            }
            catch (Exception ex)
            {
                // One malformed message must not stop the pass, and the sender deserves to
                // be told rather than left wondering.
                reply = new TelegramReply(
                    "Something went wrong applying that update. It has been logged.",
                    TelegramOutcome.Failed, ex.Message);
            }

            log.Resolve(reply.Outcome, reply.Detail, reply.BoardId, reply.UserId);
            await db.SaveChangesAsync(cancellationToken);

            if (reply.Outcome == TelegramOutcome.Applied) applied++;
            else if (reply.Outcome is not (TelegramOutcome.Command or TelegramOutcome.NoChange)) refused++;

            if (reply.ShouldReply)
            {
                // Replying is best-effort by design: the board has already been changed, and
                // a failed reply is not a reason to pretend it wasn't.
                await client.SendMessageAsync(update.ChatId, reply.Text, cancellationToken);
            }
        }

        var message = $"{updates.Count} message(s): {applied} applied, {refused} refused.";
        await settings.AcknowledgeAsync(highest, message, cancellationToken);

        return new TelegramPollReport(updates.Count, applied, refused, message);
    }
}

/// <summary>
/// Runs a message through the whole pipeline without Telegram being involved at all.
///
/// This exists because the alternative way to find out whether your template works is to
/// create a bot, link an account and type into a phone — and because an admin debugging
/// "my update didn't land" needs to reproduce it. Dry run by default: it parses, resolves
/// and authorises, then says what it *would* do.
/// </summary>
public sealed record SimulateTelegramMessageCommand(string Text, bool DryRun = true)
    : IRequest<TelegramSimulationDto>;

public sealed record TelegramSimulationDto(
    string Reply, string Outcome, string OutcomeLabel, string Detail, bool DryRun);

public sealed class SimulateTelegramMessageCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<SimulateTelegramMessageCommand, TelegramSimulationDto>
{
    public async Task<TelegramSimulationDto> Handle(
        SimulateTelegramMessageCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException("You must be signed in to do that.");
        }

        // The simulated sender is the caller's own linked Telegram account, so the test
        // exercises the real authorisation path rather than a privileged shortcut. Without
        // a link there is nothing to simulate — which is itself the answer.
        var link = await db.TelegramLinks
            .FirstOrDefaultAsync(l => l.UserId == userId && l.IsActive, cancellationToken);

        if (link is null)
        {
            return new TelegramSimulationDto(
                "Your account is not linked to Telegram yet, so there is no sender to test as. "
                + "Create an enrolment code and send it to the bot first.",
                nameof(TelegramOutcome.SenderNotLinked),
                TelegramOutcomeText.Label(TelegramOutcome.SenderNotLinked),
                "No active link for the calling user.",
                request.DryRun);
        }

        var message = new TelegramInboundMessage(
            UpdateId: 0,
            ChatId: 0,
            ChatType: "private",
            SenderId: link.TelegramUserId,
            SenderName: link.DisplayName,
            SenderUsername: link.TelegramUsername,
            Text: request.Text);

        var reply = await sender.Send(
            new ProcessTelegramMessageCommand(message, request.DryRun), cancellationToken);

        return new TelegramSimulationDto(
            reply.Text,
            reply.Outcome.ToString(),
            TelegramOutcomeText.Label(reply.Outcome),
            reply.Detail,
            request.DryRun);
    }
}
