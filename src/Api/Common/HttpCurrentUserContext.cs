using System.Security.Claims;
using Application.Abstractions;
using Domain.Enums;

namespace Api.Common;

/// <summary>
/// Reads the strongly-typed identity out of the JWT claims. Kept separate from
/// <see cref="HttpCurrentUser"/>, which exists for audit stamping and deals in display
/// names; this one is what authorisation decisions are made from.
/// </summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(
            Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Principal?.FindFirstValue("sub"),
            out var id)
            ? id
            : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(
            Principal?.FindFirstValue(ClaimTypes.Role) ?? Principal?.FindFirstValue("role"),
            out var role)
            ? role
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}
