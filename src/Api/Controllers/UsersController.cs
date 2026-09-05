using Application.Common;
using Application.Users;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Accounts that can sign in (spec section 8).
///
/// Admin-only except <c>PUT me/password</c>: everyone may change their own password, and
/// that is what makes an admin-set password safe — the person can replace the one the
/// admin knows.
///
/// Authorisation lives in the handlers, not on these routes, so the rules hold whatever
/// calls them.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController(ISender sender) : ControllerBase
{
    /// <summary>Everyone who can sign in. Deactivated accounts are hidden by default.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserDto>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new ListUsersQuery(q, includeInactive, page, pageSize), cancellationToken));

    /// <summary>The access levels an account can hold, with what each one means.</summary>
    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<object>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<object>> Roles() => Ok(
        UserRoleMetadata.DisplayOrder.Select(role => new
        {
            value = (int)role,
            name = role.ToString(),
            label = UserRoleMetadata.Label(role),
            description = UserRoleMetadata.Description(role)
        }));

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> Create(
        [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await sender.Send(
            new CreateUserCommand(
                request.Email ?? string.Empty,
                request.DisplayName ?? string.Empty,
                request.Role,
                request.Password ?? string.Empty,
                request.PersonId),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { }, user);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Update(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new UpdateUserCommand(id, request.DisplayName ?? string.Empty, request.Role,
                request.PersonId),
            cancellationToken));

    /// <summary>
    /// Deactivating is preferred to deleting: boards and audit entries reference who did
    /// what, and a deleted account would leave that history pointing at nobody.
    /// </summary>
    [HttpPut("{id:guid}/active")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> SetActive(
        Guid id, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SetUserActiveCommand(id, request.IsActive), cancellationToken));

    /// <summary>Sets a new password for someone else. Ends their current session.</summary>
    [HttpPut("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetPassword(
        Guid id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(
            new ResetUserPasswordCommand(id, request.NewPassword ?? string.Empty),
            cancellationToken);

        return NoContent();
    }

    /// <summary>Changes your own password. The only route here open to every signed-in user.</summary>
    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeOwnPassword(
        [FromBody] ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(
            new ChangeOwnPasswordCommand(
                request.CurrentPassword ?? string.Empty,
                request.NewPassword ?? string.Empty),
            cancellationToken);

        return NoContent();
    }
}

public sealed record CreateUserRequest(
    string? Email, string? DisplayName, UserRole Role, string? Password, Guid? PersonId);

public sealed record UpdateUserRequest(string? DisplayName, UserRole Role, Guid? PersonId);

public sealed record SetActiveRequest(bool IsActive);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record ChangeOwnPasswordRequest(string? CurrentPassword, string? NewPassword);
