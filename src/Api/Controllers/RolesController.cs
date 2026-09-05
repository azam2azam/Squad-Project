using Application.Roles;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// The roles a squad member can hold — the values behind "Default role".
///
/// Reading is open to anyone signed in, because every picker needs the list. Writing is
/// admin-only, enforced in the handlers: a role is org-wide reference data that changes
/// what every board renders.
/// </summary>
[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SquadRoleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SquadRoleDto>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new ListRolesQuery(includeInactive), cancellationToken));

    [HttpPost]
    [ProducesResponseType<SquadRoleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SquadRoleDto>> Create(
        [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await sender.Send(
            new CreateRoleCommand(
                request.Name ?? string.Empty,
                request.Label ?? string.Empty,
                request.PluralLabel,
                request.Color ?? string.Empty),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { }, role);
    }

    [HttpPut("{value:int}")]
    [ProducesResponseType<SquadRoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SquadRoleDto>> Update(
        int value, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new UpdateRoleCommand(value, request.Label ?? string.Empty, request.PluralLabel,
                request.Color ?? string.Empty, request.OrderIndex),
            cancellationToken));

    /// <summary>
    /// Retires or restores a role. Retiring is soft: it leaves the pickers but people
    /// already holding it keep it, so historical boards keep rendering correctly.
    /// </summary>
    [HttpPut("{value:int}/active")]
    [ProducesResponseType<SquadRoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SquadRoleDto>> SetActive(
        int value, [FromBody] SetRoleActiveRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SetRoleActiveCommand(value, request.IsActive), cancellationToken));
}

public sealed record CreateRoleRequest(string? Name, string? Label, string? PluralLabel, string? Color);

public sealed record UpdateRoleRequest(string? Label, string? PluralLabel, string? Color, int OrderIndex);

public sealed record SetRoleActiveRequest(bool IsActive);
