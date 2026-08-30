using Application.Boards.Commands;
using Application.Boards.Queries;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

public class BoardHandlerTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<Board> SeedBoardAsync(
        string title = "OPD Screen Revamp",
        BoardStatus status = BoardStatus.OnTrack,
        int progress = 68,
        int orderIndex = 0)
    {
        var board = new Board(title, "VIDA HIS", "Squad Alpha", "Sprint 14",
            status, progress, "seed", orderIndex);

        var po = new Person("Nadia Al-Harbi", Role.ProductOwner);
        var dev = new Person("Huda Rahman", Role.Developer, "Angular · Signals");
        _harness.Db.People.AddRange(po, dev);

        board.AddMember(po, Role.ProductOwner);
        board.AddMember(dev, Role.Developer);

        _harness.Db.Boards.Add(board);
        await _harness.Db.SaveChangesAsync();
        return board;
    }

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_stamps_the_current_user_and_writes_an_audit_row()
    {
        var handler = new CreateBoardCommandHandler(_harness.Db, _harness.CurrentUser);

        var result = await handler.Handle(
            new CreateBoardCommand("Pharmacy Revamp", "VIDA HIS", "Squad Beta",
                "Sprint 9", BoardStatus.AtRisk, 35),
            CancellationToken.None);

        result.Title.Should().Be("Pharmacy Revamp");
        result.StatusLabel.Should().Be("At Risk");
        result.StatusColor.Should().Be("#FBBF24");
        result.CreatedBy.Should().Be("Nadia Al-Harbi");

        var audit = await _harness.Db.BoardAuditEntries.SingleAsync();
        audit.NewValue.Should().Be("Created");
        audit.ChangedBy.Should().Be("Nadia Al-Harbi");
    }

    [Fact]
    public async Task Create_appends_to_the_end_of_the_portfolio()
    {
        await SeedBoardAsync(orderIndex: 0);
        await SeedBoardAsync("Second", orderIndex: 1);

        var handler = new CreateBoardCommandHandler(_harness.Db, _harness.CurrentUser);
        var result = await handler.Handle(
            new CreateBoardCommand("Third", "VIDA HIS", "Squad Gamma", null,
                BoardStatus.OnTrack, 0),
            CancellationToken.None);

        result.OrderIndex.Should().Be(2);
    }

    [Fact]
    public async Task A_new_board_with_no_members_warns_about_its_missing_roles()
    {
        var handler = new CreateBoardCommandHandler(_harness.Db, _harness.CurrentUser);

        var result = await handler.Handle(
            new CreateBoardCommand("Empty", "VIDA HIS", "Squad Delta", null,
                BoardStatus.OnTrack, 0),
            CancellationToken.None);

        result.Warnings.Should().Contain("This squad has no Product Owner.");
        result.Warnings.Should().Contain("This squad has no Developers.");
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Get_computes_composition_server_side()
    {
        var board = await SeedBoardAsync();
        var handler = new GetBoardQueryHandler(_harness.Db);

        var result = await handler.Handle(new GetBoardQuery(board.Id), CancellationToken.None);

        result.Composition.Total.Should().Be(2);
        result.Composition.LegendText.Should().Be("1 Product Owner · 1 Developer");
        result.Composition.Segments.Sum(s => s.Percent).Should().BeApproximately(100d, 0.0001);
        result.Members.Should().HaveCount(2);
        result.Members[0].RoleColor.Should().Be("#2DD4BF");
    }

    [Fact]
    public async Task Get_throws_for_an_unknown_board()
    {
        var handler = new GetBoardQueryHandler(_harness.Db);

        var act = () => handler.Handle(new GetBoardQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_audits_status_and_progress_transitions_with_labels()
    {
        var board = await SeedBoardAsync(status: BoardStatus.OnTrack, progress: 68);
        var handler = new UpdateBoardMetaCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.Notifier);

        await handler.Handle(new UpdateBoardMetaCommand(
            board.Id, board.Title, board.Product, board.SquadName, board.Sprint,
            BoardStatus.Blocked, 42, "Waiting on sign-off"), CancellationToken.None);

        var audits = await _harness.Db.BoardAuditEntries
            .OrderBy(a => a.ChangedAt).ToListAsync();

        audits.Should().Contain(a =>
            a.Field == "Status" && a.OldValue == "On Track" && a.NewValue == "Blocked");
        audits.Should().Contain(a =>
            a.Field == "Progress" && a.OldValue == "68%" && a.NewValue == "42%");
    }

    [Fact]
    public async Task Update_writes_no_audit_row_when_status_and_progress_are_unchanged()
    {
        var board = await SeedBoardAsync();
        var handler = new UpdateBoardMetaCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.Notifier);

        // Only the title changes.
        await handler.Handle(new UpdateBoardMetaCommand(
            board.Id, "Renamed", board.Product, board.SquadName, board.Sprint,
            board.Status, board.ProgressPercent), CancellationToken.None);

        (await _harness.Db.BoardAuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_broadcasts_the_change_to_connected_viewers()
    {
        var board = await SeedBoardAsync();
        var handler = new UpdateBoardMetaCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.Notifier);

        await handler.Handle(new UpdateBoardMetaCommand(
            board.Id, board.Title, board.Product, board.SquadName, board.Sprint,
            BoardStatus.Delivered, 100), CancellationToken.None);

        _harness.Notifier.BoardUpdates.Should().ContainSingle()
            .Which.BoardId.Should().Be(board.Id);
    }

    // -----------------------------------------------------------------------
    // Duplicate / delete / reorder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_copies_membership_without_duplicating_roster_people()
    {
        var board = await SeedBoardAsync();
        var handler = new DuplicateBoardCommandHandler(_harness.Db, _harness.CurrentUser);

        var copy = await handler.Handle(new DuplicateBoardCommand(board.Id), CancellationToken.None);

        copy.Id.Should().NotBe(board.Id);
        copy.Title.Should().Be("OPD Screen Revamp (copy)");
        copy.Members.Should().HaveCount(2);

        // The roster is shared, not cloned — still just the two original people.
        (await _harness.Db.People.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Soft_deleted_boards_drop_out_of_listings_but_stay_in_the_table()
    {
        var board = await SeedBoardAsync();
        var deleteHandler = new DeleteBoardCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.Notifier);

        await deleteHandler.Handle(new DeleteBoardCommand(board.Id), CancellationToken.None);

        var listHandler = new ListBoardsQueryHandler(_harness.Db);
        var listed = await listHandler.Handle(new ListBoardsQuery(), CancellationToken.None);

        listed.TotalCount.Should().Be(0);
        (await _harness.Db.Boards.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reorder_rejects_the_whole_request_when_a_board_is_missing()
    {
        var board = await SeedBoardAsync();
        var handler = new ReorderBoardsCommandHandler(_harness.Db);

        var act = () => handler.Handle(
            new ReorderBoardsCommand([
                new BoardOrderItem(board.Id, 5),
                new BoardOrderItem(Guid.NewGuid(), 6)
            ]),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        // The valid board must not have been reordered by a request that failed.
        var unchanged = await _harness.Db.Boards.AsNoTracking()
            .SingleAsync(b => b.Id == board.Id);
        unchanged.OrderIndex.Should().Be(0);
    }

    [Fact]
    public async Task Reorder_applies_the_requested_indexes()
    {
        var first = await SeedBoardAsync("First", orderIndex: 0);
        var second = await SeedBoardAsync("Second", orderIndex: 1);
        var handler = new ReorderBoardsCommandHandler(_harness.Db);

        await handler.Handle(
            new ReorderBoardsCommand([
                new BoardOrderItem(first.Id, 1),
                new BoardOrderItem(second.Id, 0)
            ]),
            CancellationToken.None);

        var listed = await new ListBoardsQueryHandler(_harness.Db)
            .Handle(new ListBoardsQuery(), CancellationToken.None);

        listed.Items.Select(i => i.Title).Should().ContainInOrder("Second", "First");
    }

    // -----------------------------------------------------------------------
    // Listing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task List_filters_by_status()
    {
        await SeedBoardAsync("On track one", BoardStatus.OnTrack);
        await SeedBoardAsync("Blocked one", BoardStatus.Blocked);
        var handler = new ListBoardsQueryHandler(_harness.Db);

        var result = await handler.Handle(
            new ListBoardsQuery(Status: BoardStatus.Blocked), CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Single().Title.Should().Be("Blocked one");
    }

    [Fact]
    public async Task List_pages_and_reports_the_full_count()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedBoardAsync($"Board {i}", orderIndex: i);
        }

        var handler = new ListBoardsQueryHandler(_harness.Db);
        var result = await handler.Handle(
            new ListBoardsQuery(Page: 2, PageSize: 2), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.HasNext.Should().BeTrue();
        result.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public async Task List_summary_carries_the_composition_legend()
    {
        await SeedBoardAsync();
        var handler = new ListBoardsQueryHandler(_harness.Db);

        var result = await handler.Handle(new ListBoardsQuery(), CancellationToken.None);

        result.Items.Single().CompositionLegend.Should().Be("1 Product Owner · 1 Developer");
        result.Items.Single().MemberCount.Should().Be(2);
    }
}
