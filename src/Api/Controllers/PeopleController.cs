using Application.Common;
using Application.Contracts;
using Application.People.Commands;
using Application.People.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>The org-wide reusable roster (spec section 7).</summary>
[ApiController]
[Route("api/v1/people")]
[Produces("application/json")]
public sealed class PeopleController(ISender sender) : ControllerBase
{
    /// <summary>Roster listing and typeahead. Pass ?q= to search, ?includeInactive=true to see leavers.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PersonDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PersonDto>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Ok(await sender.Send(
            new ListPeopleQuery(q, includeInactive, page, pageSize), cancellationToken));

    [HttpPost]
    [ProducesResponseType<PersonDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PersonDto>> Create(
        [FromBody] CreatePersonCommand command, CancellationToken cancellationToken)
    {
        var person = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(List), new { q = person.FullName }, person);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<PersonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDto>> Update(
        Guid id, [FromBody] UpdatePersonCommand command, CancellationToken cancellationToken)
        => Ok(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary>
    /// Soft delete. The person stops appearing in the picker but their existing squad
    /// assignments stay intact, so historical boards still show who was on them.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivatePersonCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<PersonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDto>> Reactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await sender.Send(new ReactivatePersonCommand(id), cancellationToken));
}
