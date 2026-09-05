using Application.Abstractions;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

/// <summary>
/// Loads every role from the database into <see cref="RoleMetadata"/>.
///
/// Inactive roles are included deliberately: retiring a role removes it from the pickers,
/// but people already holding it must keep rendering with their proper label and colour
/// rather than degrading to a grey placeholder.
/// </summary>
public sealed class RoleCatalog(IAppDbContext db, ILogger<RoleCatalog> logger) : IRoleCatalog
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var roles = await db.SquadRoles
                .OrderBy(r => r.OrderIndex)
                .ThenBy(r => r.Value)
                .ToListAsync(cancellationToken);

            RoleMetadata.Configure(roles.Select(r => r.ToDefinition()));
        }
        catch (Exception ex)
        {
            // A failure here must not stop the app starting: the built-in seven remain
            // configured, so the app runs with its defaults rather than no roles at all.
            logger.LogError(ex,
                "Could not load the role catalogue; falling back to the built-in roles.");
        }
    }
}
