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
        CancellationToken cancellationToken = default)
    {
        if (await db.Boards.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
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

        db.Boards.Add(board);

        db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Board", null, "Created from seed data", "seed"));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed complete: 1 board, {PersonCount} people.", roster.Count);
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
