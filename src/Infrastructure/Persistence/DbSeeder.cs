using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

/// <summary>
/// Seeds the prototype example so the app is meaningful on first run:
/// OPD Screen Revamp / Squad Alpha / 6 members (spec section 5).
/// Idempotent — safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    /// <summary>Stable ids so re-seeding and JSON round-trips stay deterministic.</summary>
    private static readonly Guid DemoBoardId = new("8f1c4d10-0000-4000-a000-000000000001");

    public static async Task SeedAsync(AppDbContext db, ILogger logger,
        IPasswordHasher passwordHasher, CancellationToken cancellationToken = default)
    {
        // Users are seeded independently of boards: an existing database from before
        // auth existed still needs accounts to sign in with.
        await SeedUsersAsync(db, logger, passwordHasher, cancellationToken);

        if (await db.Boards.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            // A database seeded before ownership existed has an ownerless demo board,
            // which only an Admin could edit — that would make the Product Owner role
            // undemonstrable. Only the known demo board is adopted; real pre-existing
            // boards stay ownerless on purpose, since guessing an owner for somebody
            // else's board is worse than requiring an Admin to assign one.
            await AdoptDemoBoardAsync(db, logger, cancellationToken);

            logger.LogInformation("Seed skipped: boards already present.");
            return;
        }

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

        // Owned by the demo Product Owner rather than left ownerless, so the
        // "a PO may edit their own boards" path is demonstrable on first run.
        var productOwner = await db.Users
            .FirstOrDefaultAsync(u => u.Role == UserRole.ProductOwner, cancellationToken);
        board.AssignOwner(productOwner?.Id);

        db.Boards.Add(board);

        db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Board", null, "Created from seed data", "seed"));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed complete: 1 board, {PersonCount} people.", roster.Count);
    }

    /// <summary>
    /// One account per role so every permission path is demonstrable on first run.
    /// The password is a well-known development default and is fine to have in source
    /// precisely because it is only ever seeded in Development — production is gated by
    /// Database:SeedDemoData, which defaults to false outside Development.
    /// </summary>
    private const string DemoPassword = "Demo!Pass123";

    private static async Task SeedUsersAsync(AppDbContext db, ILogger logger,
        IPasswordHasher passwordHasher, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var hash = passwordHasher.Hash(DemoPassword);

        db.Users.AddRange(
            new AppUser("admin@pirt.example", "Ghada Al-Suwaidi", UserRole.Admin, hash),
            new AppUser("po@pirt.example", "Nadia Al-Harbi", UserRole.ProductOwner, hash),
            new AppUser("viewer@pirt.example", "Executive Viewer", UserRole.Viewer, hash));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded 3 demo accounts (admin@, po@, viewer@pirt.example). " +
            "These exist only because Database:SeedDemoData is enabled.");
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
        // Bench roster so the typeahead has people beyond the demo squad.
        ["sara"] = new Person("Sara Al-Otaibi", Role.BusinessAnalyst,
            "Clinical workflows · BPMN", "sara.alotaibi@example.com"),
        ["tariq"] = new Person("Tariq Nawaz", Role.DevOps,
            "Kubernetes · Azure DevOps", "tariq.nawaz@example.com"),
        ["mona"] = new Person("Mona Farouk", Role.QaEngineer,
            "Automation · performance", "mona.farouk@example.com")
    };
}
