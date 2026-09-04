using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

/// <summary>
/// Database seeding, split into two independent concerns:
///
/// - <b>The administrator account</b>, so a fresh install can be signed into at all.
///   Controlled by <c>Database:SeedAdminUser</c> (on by default).
/// - <b>Demo content</b> — example boards, a roster and extra role accounts.
///   Controlled by <c>Database:SeedDemoData</c> (off by default).
///
/// They are separate because a clean deployment wants the first and not the second.
/// Both are idempotent and safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    /// <summary>Stable id so demo re-seeding and JSON round-trips stay deterministic.</summary>
    private static readonly Guid DemoBoardId = new("8f1c4d10-0000-4000-a000-000000000001");

    public static async Task SeedAsync(
        AppDbContext db,
        ILogger logger,
        IPasswordHasher passwordHasher,
        SeedOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.SeedAdminUser)
        {
            await SeedAdminAsync(db, logger, passwordHasher, options, cancellationToken);
        }

        if (!options.SeedDemoData)
        {
            return;
        }

        await SeedDemoUsersAsync(db, logger, passwordHasher, options, cancellationToken);

        if (await db.Boards.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            // A database seeded before ownership existed has an ownerless demo board,
            // which only an Admin could edit — that would make the Product Owner role
            // undemonstrable. Only the known demo board is adopted; real pre-existing
            // boards stay ownerless on purpose, since guessing an owner for somebody
            // else's board is worse than requiring an Admin to assign one.
            await AdoptDemoBoardAsync(db, logger, cancellationToken);

            logger.LogInformation("Demo seed skipped: boards already present.");
            return;
        }

        await SeedDemoBoardAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Ensures exactly one administrator exists so a clean install can be signed into.
    /// Does nothing once any user is present, so it never fights with real accounts or
    /// resets a password somebody has already changed.
    /// </summary>
    private static async Task SeedAdminAsync(AppDbContext db, ILogger logger,
        IPasswordHasher passwordHasher, SeedOptions options, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var admin = new AppUser(
            options.AdminEmail,
            options.AdminDisplayName,
            UserRole.Admin,
            passwordHasher.Hash(options.AdminPassword));

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Created the initial administrator {Email}. Change this password immediately — " +
            "it comes from configuration and is not a secret.",
            options.AdminEmail);
    }

    /// <summary>The extra role accounts, so the Product Owner and Viewer paths are demoable.</summary>
    private static async Task SeedDemoUsersAsync(AppDbContext db, ILogger logger,
        IPasswordHasher passwordHasher, SeedOptions options, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Role != UserRole.Admin, cancellationToken))
        {
            return;
        }

        var hash = passwordHasher.Hash(options.AdminPassword);

        db.Users.AddRange(
            new AppUser("po@pirt.example", "Nadia Al-Harbi", UserRole.ProductOwner, hash),
            new AppUser("viewer@pirt.example", "Executive Viewer", UserRole.Viewer, hash));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the demo Product Owner and Viewer accounts.");
    }

    private static async Task SeedDemoBoardAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding demo data (OPD Screen Revamp / Squad Alpha).");

        var roster = BuildRoster();
        db.People.AddRange(roster.Values);

        var board = new Board(
            title: "OPD Screen Revamp",
            product: "VIDA HIS",
            squadName: "Squad Alpha",
            sprint: "Sprint 14",
            status: BoardStatus.OnTrack,
            progressPercent: 68,
            createdBy: "seed",
            orderIndex: 0);

        board.AssignIdForImport(DemoBoardId);

        board.AddMember(roster["nadia"], Role.ProductOwner, "Outpatient journey · KPI owner");
        board.AddMember(roster["faisal"], Role.TechLead, "Angular · FHIR R4");
        board.AddMember(roster["huda"], Role.Developer, "Angular · Signals");
        board.AddMember(roster["omar"], Role.Developer, ".NET 8 · EF Core");
        board.AddMember(roster["layla"], Role.QaEngineer, "Playwright · HL7 fixtures");
        board.AddMember(roster["yousef"], Role.UxDesigner, "Design system · WCAG AA");

        var productOwner = await db.Users
            .FirstOrDefaultAsync(u => u.Role == UserRole.ProductOwner, cancellationToken);
        board.AssignOwner(productOwner?.Id);

        db.Boards.Add(board);

        db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Board", null, "Created from seed data", "seed"));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo seed complete: 1 board, {PersonCount} people.", roster.Count);
    }

    /// <summary>
    /// Gives the demo board to the demo Product Owner if it has no owner yet.
    /// Idempotent, and scoped to the one known seed id.
    /// </summary>
    private static async Task AdoptDemoBoardAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        var demo = await db.Boards
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == DemoBoardId && b.OwnerId == null, cancellationToken);

        if (demo is null) return;

        var productOwner = await db.Users
            .FirstOrDefaultAsync(u => u.Role == UserRole.ProductOwner, cancellationToken);

        if (productOwner is null) return;

        demo.AssignOwner(productOwner.Id);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Assigned the demo board to {Email}.", productOwner.Email);
    }

    private static Dictionary<string, Person> BuildRoster() => new()
    {
        ["nadia"] = new Person("Nadia Al-Harbi", Role.ProductOwner,
            "Outpatient journey · KPI owner", "nadia.alharbi@example.com"),
        ["faisal"] = new Person("Faisal Al-Qahtani", Role.TechLead,
            "Angular · FHIR R4", "faisal.alqahtani@example.com"),
        ["huda"] = new Person("Huda Rahman", Role.Developer,
            "Angular · Signals", "huda.rahman@example.com"),
        ["omar"] = new Person("Omar Siddiqui", Role.Developer,
            ".NET 8 · EF Core", "omar.siddiqui@example.com"),
        ["layla"] = new Person("Layla Mansour", Role.QaEngineer,
            "Playwright · HL7 fixtures", "layla.mansour@example.com"),
        ["yousef"] = new Person("Yousef Baraka", Role.UxDesigner,
            "Design system · WCAG AA", "yousef.baraka@example.com"),
        ["sara"] = new Person("Sara Al-Otaibi", Role.BusinessAnalyst,
            "Clinical workflows · BPMN", "sara.alotaibi@example.com"),
        ["tariq"] = new Person("Tariq Nawaz", Role.DevOps,
            "Kubernetes · Azure DevOps", "tariq.nawaz@example.com"),
        ["mona"] = new Person("Mona Farouk", Role.QaEngineer,
            "Automation · performance", "mona.farouk@example.com")
    };
}

/// <summary>Seeding configuration, bound from the Database section.</summary>
public sealed class SeedOptions
{
    /// <summary>Create an administrator when the database has no users at all.</summary>
    public bool SeedAdminUser { get; init; } = true;

    /// <summary>Create example boards, a roster and the extra role accounts.</summary>
    public bool SeedDemoData { get; init; }

    public string AdminEmail { get; init; } = "admin@pirt.example";

    public string AdminDisplayName { get; init; } = "Administrator";

    /// <summary>
    /// Only ever used the first time, when no user exists. Supply a real one via
    /// Database__AdminPassword; the default is a placeholder to be changed on first login.
    /// </summary>
    public string AdminPassword { get; init; } = "Admin!Pass123";
}
