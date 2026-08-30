using Application.Abstractions;
using Application.Auth.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Local sign-in (spec section 8). This is the login stub the spec calls for; the token
/// shape and claims are OIDC-compatible, so federating later replaces this controller
/// without changing anything downstream.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(ISender sender, ICurrentUserContext currentUser)
    : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Login(
        [FromBody] LoginCommand command, CancellationToken cancellationToken)
        => Ok(await sender.Send(command, cancellationToken));

    /// <summary>Exchanges a refresh token for a new pair. The old refresh token is rotated out.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken));

    /// <summary>Revokes the stored refresh token so the session cannot be renewed.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } id)
        {
            await sender.Send(new LogoutCommand(id), cancellationToken);
        }

        return NoContent();
    }

    /// <summary>Who the bearer token belongs to. Used by the client to restore a session.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<SignedInUser>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignedInUser>> Me(CancellationToken cancellationToken)
    {
        var user = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }
}

public sealed record RefreshRequest(string RefreshToken);
