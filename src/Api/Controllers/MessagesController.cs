using Application.Messaging;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Team messaging: board channels, squad channels and direct threads.
///
/// Every route requires a signed-in user and none takes a user id from the caller — who
/// you are is read from the token, never from the request body. A messaging API that lets
/// the client say who it is is one where anybody can read anybody's inbox.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Produces("application/json")]
[Authorize]
public sealed class MessagesController(ISender sender) : ControllerBase
{
    /// <summary>The caller's conversations, unread first.</summary>
    [HttpGet("inbox")]
    [ProducesResponseType<IReadOnlyList<ConversationSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryDto>>> Inbox(
        [FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetInboxQuery(q), cancellationToken));

    /// <summary>Counts for the navigation badge.</summary>
    [HttpGet("unread")]
    [ProducesResponseType<UnreadSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadSummaryDto>> Unread(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetUnreadSummaryQuery(), cancellationToken));

    /// <summary>Everywhere a message could be sent — boards, squads and colleagues.</summary>
    [HttpGet("targets")]
    [ProducesResponseType<IReadOnlyList<MessageTargetDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MessageTargetDto>>> Targets(
        [FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMessageTargetsQuery(q), cancellationToken));

    [HttpGet("{conversationId:guid}")]
    [ProducesResponseType<ConversationThreadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationThreadDto>> Thread(
        Guid conversationId, [FromQuery] int take = 200,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetConversationQuery(conversationId, take), cancellationToken));

    // -----------------------------------------------------------------------
    // Opening a conversation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the channel for a board, creating it on first use. POST rather than GET
    /// because it may write, even though it reads like a lookup.
    /// </summary>
    [HttpPost("boards/{boardId:guid}")]
    [ProducesResponseType<ConversationRefResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationRefResponse>> OpenBoard(
        Guid boardId, CancellationToken cancellationToken) =>
        Ok(new ConversationRefResponse(
            await sender.Send(new OpenBoardConversationCommand(boardId), cancellationToken)));

    [HttpPost("squads")]
    [ProducesResponseType<ConversationRefResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationRefResponse>> OpenSquad(
        [FromBody] OpenSquadRequest request, CancellationToken cancellationToken) =>
        Ok(new ConversationRefResponse(
            await sender.Send(new OpenSquadConversationCommand(request.SquadName ?? string.Empty),
                cancellationToken)));

    [HttpPost("direct/{userId:guid}")]
    [ProducesResponseType<ConversationRefResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationRefResponse>> OpenDirect(
        Guid userId, CancellationToken cancellationToken) =>
        Ok(new ConversationRefResponse(
            await sender.Send(new OpenDirectConversationCommand(userId), cancellationToken)));

    // -----------------------------------------------------------------------
    // Writing
    // -----------------------------------------------------------------------

    [HttpPost("{conversationId:guid}/posts")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MessageDto>> Post(
        Guid conversationId, [FromBody] PostMessageRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new PostMessageCommand(conversationId, request.Body ?? string.Empty, request.ReplyToMessageId),
            cancellationToken));

    [HttpPut("posts/{messageId:guid}")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MessageDto>> Edit(
        Guid messageId, [FromBody] EditMessageRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new EditMessageCommand(messageId, request.Body ?? string.Empty),
            cancellationToken));

    [HttpDelete("posts/{messageId:guid}")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MessageDto>> Delete(
        Guid messageId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new DeleteMessageCommand(messageId), cancellationToken));

    // -----------------------------------------------------------------------
    // Housekeeping
    // -----------------------------------------------------------------------

    [HttpPost("{conversationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid conversationId, CancellationToken cancellationToken)
    {
        await sender.Send(new MarkConversationReadCommand(conversationId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{conversationId:guid}/mute")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Mute(Guid conversationId,
        [FromBody] MuteRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new SetConversationMutedCommand(conversationId, request.Muted),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{conversationId:guid}/membership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Leave(Guid conversationId, CancellationToken cancellationToken)
    {
        await sender.Send(new LeaveConversationCommand(conversationId), cancellationToken);
        return NoContent();
    }
}

public sealed record ConversationRefResponse(Guid ConversationId);

public sealed record OpenSquadRequest(string? SquadName);

public sealed record PostMessageRequest(string? Body, Guid? ReplyToMessageId);

public sealed record EditMessageRequest(string? Body);

public sealed record MuteRequest(bool Muted);
