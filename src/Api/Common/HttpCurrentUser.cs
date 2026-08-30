using System.Security.Claims;
using Application.Abstractions;

namespace Api.Common;

/// <summary>
/// Reads identity from the current HTTP request. Until JWT lands in M5 this reports
/// an anonymous principal, which is enough for audit stamping and keeps the
/// Application layer written against the final shape.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub");

    public string DisplayName =>
        Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue("preferred_username")
        ?? "system";

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
