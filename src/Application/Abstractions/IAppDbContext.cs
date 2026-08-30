using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions;

/// <summary>
/// The persistence surface the Application layer is allowed to see. Infrastructure
/// implements it with EF Core; no EF provider types cross into Domain.
/// </summary>
public interface IAppDbContext
{
    DbSet<Board> Boards { get; }
    DbSet<Person> People { get; }
    DbSet<SquadMember> SquadMembers { get; }
    DbSet<BoardAuditEntry> BoardAuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
