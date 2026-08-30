using Application.Portability;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

public class PortabilityTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<Board> SeedAsync()
    {
        var board = new Board("OPD Screen Revamp", "VIDA HIS", "Squad Alpha", "Sprint 14",
            BoardStatus.OnTrack, 68, "seed");

        var po = new Person("Nadia Al-Harbi", Role.ProductOwner, "Outpatient journey");
        var dev = new Person("Huda Rahman", Role.Developer, "Angular · Signals");
        var bench = new Person("Tariq Nawaz", Role.DevOps, "Kubernetes");
        _harness.Db.People.AddRange(po, dev, bench);

        board.AddMember(po, Role.ProductOwner);
        board.AddMember(dev, Role.Developer, "Angular");

        _harness.Db.Boards.Add(board);
        await _harness.Db.SaveChangesAsync();
        return board;
    }

    private Task<BoardExportFile> ExportAsync() =>
        new ExportDataQueryHandler(_harness.Db).Handle(new ExportDataQuery(), CancellationToken.None);

    private Task<ImportResult> ImportAsync(BoardExportFile file) =>
        new ImportDataCommandHandler(_harness.Db, _harness.CurrentUser)
            .Handle(new ImportDataCommand(file), CancellationToken.None);

    [Fact]
    public async Task Export_carries_boards_members_and_the_whole_roster()
    {
        await SeedAsync();

        var file = await ExportAsync();

        file.Version.Should().Be(BoardExportFile.CurrentVersion);
        file.Boards.Should().ContainSingle();
        file.Boards[0].Members.Should().HaveCount(2);
        // Including the bench member who is on no board — otherwise the roster
        // would shrink every time it round-trips.
        file.People.Should().HaveCount(3);
    }

    [Fact]
    public async Task Re_importing_an_unchanged_export_creates_nothing()
    {
        await SeedAsync();
        var file = await ExportAsync();

        var result = await ImportAsync(file);

        result.BoardsCreated.Should().Be(0);
        result.PeopleCreated.Should().Be(0);
        result.BoardsUpdated.Should().Be(1);
        result.PeopleUpdated.Should().Be(3);

        (await _harness.Db.Boards.CountAsync()).Should().Be(1);
        (await _harness.Db.People.CountAsync()).Should().Be(3);
        (await _harness.Db.SquadMembers.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Importing_into_an_empty_database_restores_everything()
    {
        await SeedAsync();
        var file = await ExportAsync();

        // A second, empty database stands in for "restore onto a fresh install".
        using var fresh = new TestHarness();
        var result = await new ImportDataCommandHandler(fresh.Db, fresh.CurrentUser)
            .Handle(new ImportDataCommand(file), CancellationToken.None);

        result.BoardsCreated.Should().Be(1);
        result.PeopleCreated.Should().Be(3);
        result.MembersLinked.Should().Be(2);

        var restored = await fresh.Db.Boards
            .Include(b => b.Members).ThenInclude(m => m.Person)
            .SingleAsync();

        restored.Title.Should().Be("OPD Screen Revamp");
        restored.ProgressPercent.Should().Be(68);
        restored.Composition.LegendText.Should().Be("1 Product Owner · 1 Developer");
        restored.Members.Single(m => m.Role == Role.Developer).Detail.Should().Be("Angular");
    }

    [Fact]
    public async Task Import_removes_members_no_longer_listed_in_the_file()
    {
        var board = await SeedAsync();
        var file = await ExportAsync();

        // Drop the developer from the file.
        var trimmed = file with
        {
            Boards = [file.Boards[0] with
            {
                Members = file.Boards[0].Members
                    .Where(m => m.Role == Role.ProductOwner).ToList()
            }]
        };

        await ImportAsync(trimmed);

        var reloaded = await _harness.Db.Boards
            .Include(b => b.Members)
            .SingleAsync(b => b.Id == board.Id);

        reloaded.Members.Should().ContainSingle()
            .Which.Role.Should().Be(Role.ProductOwner);
    }

    [Fact]
    public async Task A_member_referencing_an_unknown_person_is_skipped_with_a_warning()
    {
        await SeedAsync();
        var file = await ExportAsync();

        var corrupted = file with
        {
            Boards = [file.Boards[0] with
            {
                Members = [.. file.Boards[0].Members,
                    new ExportedMember(Guid.NewGuid(), Role.QaEngineer, "Ghost", null, 9)]
            }]
        };

        var result = await ImportAsync(corrupted);

        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("unknown person");
        // The rest of the import still applied.
        result.MembersLinked.Should().Be(2);
    }

    [Fact]
    public async Task Deactivated_people_survive_a_round_trip()
    {
        await SeedAsync();
        var bench = await _harness.Db.People.SingleAsync(p => p.FullName == "Tariq Nawaz");
        bench.Deactivate();
        await _harness.Db.SaveChangesAsync();

        var file = await ExportAsync();
        file.People.Single(p => p.FullName == "Tariq Nawaz").IsActive.Should().BeFalse();

        using var fresh = new TestHarness();
        await new ImportDataCommandHandler(fresh.Db, fresh.CurrentUser)
            .Handle(new ImportDataCommand(file), CancellationToken.None);

        var restored = await fresh.Db.People.SingleAsync(p => p.FullName == "Tariq Nawaz");
        restored.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task An_imported_board_can_keep_a_member_who_has_since_left()
    {
        var board = await SeedAsync();

        // The developer leaves, but the historical board must still show them.
        var dev = await _harness.Db.People.SingleAsync(p => p.FullName == "Huda Rahman");
        dev.Deactivate();
        await _harness.Db.SaveChangesAsync();

        var file = await ExportAsync();

        using var fresh = new TestHarness();
        var result = await new ImportDataCommandHandler(fresh.Db, fresh.CurrentUser)
            .Handle(new ImportDataCommand(file), CancellationToken.None);

        result.MembersLinked.Should().Be(2);
        result.Warnings.Should().BeEmpty();

        var restored = await fresh.Db.Boards.Include(b => b.Members).SingleAsync();
        restored.Members.Should().HaveCount(2);
        _ = board;
    }
}
