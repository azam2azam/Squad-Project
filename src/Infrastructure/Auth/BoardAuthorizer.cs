using Application.Abstractions;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Auth;

/// <summary>
/// Enforces the ownership rules that a role claim alone cannot express (spec section 8).
///
/// Deliberately server-side and applied in the handlers rather than only on routes: the
/// frontend guards are a convenience, and a viewer who crafts a request by hand must
/// still be refused.
/// </summary>
public sealed class BoardAuthorizer(IAppDbContext db, ICurrentUserContext user) : IBoardAuthorizer
{
    public void EnsureCanCreate()
    {
        RequireAuthentication();

        if (user.Role is UserRole.Viewer)
        {
            throw new ForbiddenException("Viewers have read-only access.");
        }
    }

    public void EnsureIsAdmin()
    {
        RequireAuthentication();

        if (user.Role is not UserRole.Admin)
        {
            throw new ForbiddenException("That action requires an administrator.");
        }
    }

    public async Task EnsureCanEditAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        RequireAuthentication();

        if (user.Role is UserRole.Admin)
        {
            return;
        }

        if (user.Role is UserRole.Viewer)
        {
            throw new ForbiddenException("Viewers have read-only access.");
        }

        // Product Owner: only their own boards.
        var ownerId = await db.Boards
            .Where(b => b.Id == boardId)
            .Select(b => b.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerId != user.UserId)
        {
            throw new ForbiddenException(
                "You can only edit boards you own. Ask an administrator to reassign it.");
        }
    }

    private void RequireAuthentication()
    {
        if (!user.IsAuthenticated || user.UserId is null)
        {
            throw new UnauthorizedException("You must be signed in to do that.");
        }
    }
}
