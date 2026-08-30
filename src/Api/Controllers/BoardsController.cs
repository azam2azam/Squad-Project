using Application.Boards.Commands;
using Application.Boards.Queries;
using Application.Common;
using Application.Contracts;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Board CRUD and ordering (spec section 7).</summary>
[ApiController]
[Route("api/v1/boards")]
[Produces("application/json")]
public sealed class BoardsController(ISender sender) : ControllerBase
{
    /// <summary>Portfolio listing, paginated, with optional search and status filter.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BoardSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BoardSummaryDto>>> List(
        [FromQuery] string? q,
        [FromQuery] BoardStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new ListBoardsQuery(q, status, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BoardDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetBoardQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType<BoardDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BoardDetailDto>> Create(
        [FromBody] CreateBoardCommand command, CancellationToken cancellationToken)
    {
        var board = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = board.Id }, board);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<BoardDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardDetailDto>> Update(
        Guid id, [FromBody] UpdateBoardMetaCommand command, CancellationToken cancellationToken)
    {
        // The route is authoritative; a mismatched body id is a client bug, not an override.
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Route and body ids do not match",
                Detail = $"Route id {id} does not match body id {command.Id}."
            });
        }

        return Ok(await sender.Send(command with { Id = id }, cancellationToken));
    }

    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType<BoardDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardDetailDto>> Duplicate(
        Guid id, [FromBody] DuplicateBoardRequest? request, CancellationToken cancellationToken)
    {
        var board = await sender.Send(
            new DuplicateBoardCommand(id, request?.NewTitle), cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = board.Id }, board);
    }

    /// <summary>Soft delete. The board and its audit history remain resolvable.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteBoardCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>The board's change log (spec FR-10).</summary>
    [HttpGet("{id:guid}/audit")]
    [ProducesResponseType<IReadOnlyList<BoardAuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BoardAuditEntryDto>>> Audit(
        Guid id, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new GetBoardAuditQuery(id, limit), cancellationToken));

    /// <summary>
    /// Pulls Jira and returns a suggestion. Never writes to the board — the Product
    /// Owner reviews the numbers and accepts them with a normal update (spec section 10).
    /// </summary>
    [HttpPost("{id:guid}/jira/sync")]
    [ProducesResponseType<JiraSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JiraSuggestionDto>> JiraSync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetJiraSuggestionQuery(id), cancellationToken));

    [HttpPut("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(
        [FromBody] IReadOnlyList<BoardOrderItem> items, CancellationToken cancellationToken)
    {
        await sender.Send(new ReorderBoardsCommand(items), cancellationToken);
        return NoContent();
    }
}

/// <summary>Optional body for duplicate, so the copy can be named at creation time.</summary>
public sealed record DuplicateBoardRequest(string? NewTitle);
