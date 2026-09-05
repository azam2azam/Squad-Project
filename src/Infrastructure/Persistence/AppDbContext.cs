using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// EF Core context. Configuration lives in <see cref="Configurations"/> so this class
/// stays a wiring point rather than a schema dumping ground.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<SquadMember> SquadMembers => Set<SquadMember>();
    public DbSet<BoardAuditEntry> BoardAuditEntries => Set<BoardAuditEntry>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<JiraSettings> JiraSettings => Set<JiraSettings>();
    public DbSet<SquadRole> SquadRoles => Set<SquadRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
