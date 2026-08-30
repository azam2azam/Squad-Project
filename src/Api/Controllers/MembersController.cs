using Application.Boards.Queries;
using Application.Contracts;
using Application.Members.Commands;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Squad membership on a board (spec section 7).</summary>
[ApiController]
[Produces("application/json")]
public sealed class MembersController(ISender sender) : ControllerBase
{
    [HttpGet("api/v1/boards/{boardId:guid}/members")]
    [ProducesResponseType<IReadOnlyList<SquadMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SquadMemberDto>>> List(
        Guid boardId, CancellationToken cancellationToken)
    {
        var board = await sender.Send(new GetBoardQuery(boardId), cancellationToken);
        return Ok(board.Members);
    }

    /// <summary>
    /// Adds a person to the squad, either by roster id or by quick-creating them inline.
    /// </summary>
    [HttpPost("api/v1/boards/{boardId:guid}/members")]
    [ProducesResponseType<SquadMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SquadMemberDto>> Add(
        Guid boardId, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await sender.Send(
            new AddMemberCommand(boardId, request.PersonId, request.NewPerson,
                request.Role, request.Detail, request.AllocationPercent),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { boardId }, member);
    }

    [HttpPut("api/v1/members/{id:guid}")]
    [ProducesResponseType<SquadMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SquadMemberDto>> Update(
        Guid id, [FromBody] UpdateMemberRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new UpdateMemberCommand(id, request.Role, request.Detail, request.AllocationPercent),
            cancellationToken));

    [HttpDelete("api/v1/members/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveMemberCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPut("api/v1/boards/{boardId:guid}/members/reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(
        Guid boardId, [FromBody] IReadOnlyList<Guid> orderedMemberIds,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ReorderMembersCommand(boardId, orderedMemberIds), cancellationToken);
        return NoContent();
    }
}

/// <summary>Body for adding a member: supply either personId or newPerson, never both.</summary>
public sealed record AddMemberRequest(
    Guid? PersonId,
    NewPersonInput? NewPerson,
    Role Role,
    string? Detail,
    int? AllocationPercent);

public sealed record UpdateMemberRequest(Role Role, string? Detail, int? AllocationPercent);
