using Application.Abstractions;
using Domain.Enums;
using Infrastructure.Auth;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Application.Tests;

/// <summary>
/// A real relational database per test, held in memory by SQLite.
/// Chosen over the InMemory provider so query filters, foreign keys and unique
/// indexes actually behave the way they will in production.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestHarness()
    {
        // The connection must stay open: closing it drops the in-memory database.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new SqliteAppDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>
    /// SQLite has no native DateTimeOffset, so it refuses to ORDER BY one. Production
    /// runs on SQL Server where this is a non-issue; storing the value as binary here
    /// keeps the harness faithful without weakening the real queries.
    /// </summary>
    private sealed class SqliteAppDbContext(DbContextOptions<AppDbContext> options)
        : AppDbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            base.ConfigureConventions(builder);
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    public AppDbContext Db { get; }

    public ICurrentUser CurrentUser { get; } = new FakeCurrentUser("Nadia Al-Harbi");

    /// <summary>
    /// Defaults to an Admin so tests written before RBAC existed keep exercising the
    /// behaviour they were written for. Permission tests call <see cref="AsRole"/>.
    /// </summary>
    public FakeUserContext UserContext { get; } = new(Guid.NewGuid(), UserRole.Admin);

    public IBoardAuthorizer Authorizer => new BoardAuthorizer(Db, UserContext);

    /// <summary>Switches the ambient identity, for tests that assert on permissions.</summary>
    public TestHarness AsRole(UserRole role, Guid? userId = null)
    {
        UserContext.Role = role;
        UserContext.UserId = userId ?? UserContext.UserId;
        return this;
    }

    public RecordingBoardNotifier Notifier { get; } = new();

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

public sealed class FakeCurrentUser(string displayName) : ICurrentUser
{
    public string? UserId => "test-user";
    public string DisplayName => displayName;
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => true;
}

/// <summary>Captures what would have gone out over SignalR, so handlers can assert on it.</summary>
public sealed class RecordingBoardNotifier : IBoardNotifier
{
    public List<(Guid BoardId, object Payload)> BoardUpdates { get; } = [];
    public List<(Guid BoardId, object Payload)> MemberChanges { get; } = [];

    public Task BoardUpdatedAsync(Guid boardId, object payload, CancellationToken cancellationToken = default)
    {
        BoardUpdates.Add((boardId, payload));
        return Task.CompletedTask;
    }

    public Task MemberChangedAsync(Guid boardId, object payload, CancellationToken cancellationToken = default)
    {
        MemberChanges.Add((boardId, payload));
        return Task.CompletedTask;
    }
}

/// <summary>Mutable ambient identity so a single test can switch roles mid-flight.</summary>
public sealed class FakeUserContext(Guid userId, UserRole role) : ICurrentUserContext
{
    public Guid? UserId { get; set; } = userId;
    public UserRole? Role { get; set; } = role;
    public bool IsAuthenticated => UserId is not null;
}
