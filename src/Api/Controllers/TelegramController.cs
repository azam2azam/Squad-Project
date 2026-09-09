using Application.Abstractions;
using Application.Telegram;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Administering the Telegram bot, and connecting your own account to it.
///
/// Mostly admin-only, for the same reason as Jira and Smartsheet: these endpoints handle a
/// credential that acts for the whole organisation. The exception is the enrolment code
/// for your own account, which anybody signed in may create — needing an administrator
/// present to connect your own phone is the kind of friction that quietly kills an
/// integration.
/// </summary>
[ApiController]
[Route("api/v1/integrations/telegram")]
[Produces("application/json")]
[Authorize]
public sealed class TelegramController(
    ITelegramSettingsService settingsService,
    ITelegramClient client,
    IBoardAuthorizer authorizer,
    ISender sender) : ControllerBase
{
    /// <summary>The current connection, with the bot token masked.</summary>
    [HttpGet]
    [ProducesResponseType<TelegramSettingsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramSettingsView>> Get(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        return Ok(await settingsService.GetAsync(cancellationToken));
    }

    /// <summary>
    /// Saves the connection. Leave <c>botToken</c> empty to keep the stored one — the
    /// client is never given the token, so it cannot send it back.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<TelegramSettingsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramSettingsView>> Save(
        [FromBody] SaveTelegramSettingsRequest request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var saved = await settingsService.SaveAsync(
            new SaveTelegramSettings(
                request.BaseUrl ?? string.Empty,
                request.BotToken,
                request.Enabled,
                request.AllowedChatIds,
                request.ReplyToUnknownSenders),
            cancellationToken);

        return Ok(saved);
    }

    /// <summary>Forgets the connection entirely, including the stored token.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        await settingsService.ClearAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Asks Telegram who this token belongs to, so "not configured" can be told apart from
    /// "configured with the wrong token" — and so the settings screen can show the @name
    /// people need to message.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType<TelegramConnectionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramConnectionDto>> Test(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var identity = await client.GetMeAsync(cancellationToken);

        if (identity is null)
        {
            return Ok(new TelegramConnectionDto(false, null, null,
                "Telegram did not accept that token, or could not be reached. Check the token, "
                + "and that this server can reach api.telegram.org."));
        }

        await settingsService.RecordBotUsernameAsync(identity.Username, cancellationToken);

        return Ok(new TelegramConnectionDto(true, identity.Username, identity.Name,
            $"Connected as @{identity.Username}."));
    }

    /// <summary>
    /// Reads whatever is waiting, right now, instead of waiting for the background poll.
    /// Short timeout: this is somebody watching a screen, not a long poll.
    /// </summary>
    [HttpPost("poll")]
    [ProducesResponseType<TelegramPollReport>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramPollReport>> Poll(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        return Ok(await sender.Send(new PollTelegramCommand(TimeoutSeconds: 0), cancellationToken));
    }

    /// <summary>
    /// Runs a message through the real pipeline without Telegram being involved. Dry run
    /// unless <c>dryRun</c> is explicitly false.
    /// </summary>
    [HttpPost("simulate")]
    [ProducesResponseType<TelegramSimulationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramSimulationDto>> Simulate(
        [FromBody] SimulateTelegramRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SimulateTelegramMessageCommand(request.Text ?? string.Empty, request.DryRun),
            cancellationToken));

    /// <summary>
    /// Issues a one-time enrolment code. Omit <c>userId</c> for your own account; naming
    /// somebody else requires an administrator.
    /// </summary>
    [HttpPost("enrolments")]
    [ProducesResponseType<TelegramEnrolmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TelegramEnrolmentDto>> CreateEnrolment(
        [FromBody] CreateEnrolmentRequest? request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CreateTelegramEnrolmentCommand(request?.UserId), cancellationToken));

    /// <summary>Everyone whose Telegram account is connected, and to whom.</summary>
    [HttpGet("links")]
    [ProducesResponseType<IReadOnlyList<TelegramLinkDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TelegramLinkDto>>> Links(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTelegramLinksQuery(), cancellationToken));

    /// <summary>Cuts off one Telegram account. Their application login is untouched.</summary>
    [HttpDelete("links/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeLink(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeTelegramLinkCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>What the bot has been sent, and what it made of each message.</summary>
    [HttpGet("messages")]
    [ProducesResponseType<IReadOnlyList<TelegramMessageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TelegramMessageDto>>> Messages(
        [FromQuery] int take = 40, CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetTelegramMessagesQuery(take), cancellationToken));

    /// <summary>Gives every board without a code one derived from its title.</summary>
    [HttpPost("board-codes")]
    [ProducesResponseType<IReadOnlyList<BoardCodeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BoardCodeDto>>> AssignBoardCodes(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new AssignBoardCodesCommand(), cancellationToken));

    /// <summary>The message template, so the UI and the bot cannot print different ones.</summary>
    [HttpGet("template")]
    [AllowAnonymous]
    [ProducesResponseType<TemplateDto>(StatusCodes.Status200OK)]
    public ActionResult<TemplateDto> Template() =>
        Ok(new TemplateDto(TelegramMessageParser.Template));
}

public sealed record SaveTelegramSettingsRequest(
    string? BaseUrl,
    string? BotToken,
    bool Enabled,
    string? AllowedChatIds,
    bool ReplyToUnknownSenders);

public sealed record TelegramConnectionDto(bool Connected, string? Username, string? Name,
    string Message);

public sealed record SimulateTelegramRequest(string? Text, bool DryRun = true);

public sealed record CreateEnrolmentRequest(Guid? UserId);

public sealed record TemplateDto(string Template);
