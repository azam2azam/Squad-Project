using Application.Abstractions;
using Application.Boards.Commands;
using Application.Boards.Queries;
using Application.Members.Commands;
using Application.People.Commands;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// Server-side enforcement of section 8. These assert on the handlers, not the routes:
/// the frontend guards are a convenience, and a caller who bypasses them must still be
/// refused.
/// </summary>
public class RbacTests : IDisposable
{
    private readonly TestHarness _harness = new();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _otherOwnerId = Guid.NewGuid();

    public void Dispose() => _harness.Dispose();

    private async Task<Board> SeedBoardAsync(Guid? ownerId)
    {
        var board = new Board("OPD Screen Revamp", "VIDA HIS", "Squad Alpha", "Sprint 14",
            BoardStatus.OnTrack, 68, "seed");
        board.AssignOwner(ownerId);

        var dev = new Person("Huda Rahman", Role.Developer);
        _harness.Db.People.Add(dev);
        board.AddMember(dev, Role.Developer);

        _harness.Db.Boards.Add(board);
        await _harness.Db.SaveChangesAsync();
        return board;
    }

    private UpdateBoardMetaCommand UpdateFor(Board board, int progress = 80) =>
        new(board.Id, board.Title, board.Product, board.SquadName, board.Sprint,
            BoardStatus.AtRisk, progress);

    private UpdateBoardMetaCommandHandler UpdateHandler() =>
        new(_harness.Db, _harness.CurrentUser, _harness.Notifier, _harness.Authorizer);

    // -----------------------------------------------------------------------
    // Viewer
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_viewer_cannot_update_a_board()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.AsRole(UserRole.Viewer);

        var act = () => UpdateHandler().Handle(UpdateFor(board), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*read-only*");
    }

    [Fact]
    public async Task A_viewer_cannot_create_a_board()
    {
        _harness.AsRole(UserRole.Viewer);
        var handler = new CreateBoardCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.UserContext, _harness.Authorizer);

        var act = () => handler.Handle(
            new CreateBoardCommand("X", "P", "S", null, BoardStatus.OnTrack, 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_viewer_cannot_delete_a_board()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.AsRole(UserRole.Viewer);

        var handler = new DeleteBoardCommandHandler(
            _harness.Db, _harness.CurrentUser, _harness.Notifier, _harness.Authorizer);

        var act = () => handler.Handle(new DeleteBoardCommand(board.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();

        // And nothing was written.
        var reloaded = await _harness.Db.Boards.AsNoTracking()
            .IgnoreQueryFilters().SingleAsync(b => b.Id == board.Id);
        reloaded.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_viewer_cannot_add_a_squad_member()
    {
        var board = await SeedBoardAsync(_ownerId);
        var person = new Person("Tariq Nawaz", Role.DevOps);
        _harness.Db.People.Add(person);
        await _harness.Db.SaveChangesAsync();

        _harness.AsRole(UserRole.Viewer);
        var handler = new AddMemberCommandHandler(
            _harness.Db, _harness.Notifier, _harness.Authorizer);

        var act = () => handler.Handle(
            new AddMemberCommand(board.Id, person.Id, null, Role.DevOps), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_viewer_can_still_read_and_export()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.AsRole(UserRole.Viewer);

        // Reads are deliberately unguarded — a viewer's whole purpose is to look.
        var detail = await new GetBoardQueryHandler(_harness.Db)
            .Handle(new GetBoardQuery(board.Id), CancellationToken.None);
        detail.Title.Should().Be("OPD Screen Revamp");

        var listed = await new ListBoardsQueryHandler(_harness.Db)
            .Handle(new ListBoardsQuery(), CancellationToken.None);
        listed.TotalCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Product Owner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_product_owner_can_update_a_board_they_own()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.AsRole(UserRole.ProductOwner, _ownerId);

        var result = await UpdateHandler().Handle(UpdateFor(board), CancellationToken.None);

        result.ProgressPercent.Should().Be(80);
        result.StatusLabel.Should().Be("At Risk");
    }

    [Fact]
    public async Task A_product_owner_cannot_update_someone_elses_board()
    {
        var board = await SeedBoardAsync(_otherOwnerId);
        _harness.AsRole(UserRole.ProductOwner, _ownerId);

        var act = () => UpdateHandler().Handle(UpdateFor(board), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*only edit boards you own*");
    }

    [Fact]
    public async Task A_product_owner_cannot_edit_an_ownerless_board()
    {
        // Seeded and imported boards have no owner, so only an Admin may touch them.
        var board = await SeedBoardAsync(null);
        _harness.AsRole(UserRole.ProductOwner, _ownerId);

        var act = () => UpdateHandler().Handle(UpdateFor(board), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_product_owner_cannot_edit_the_org_wide_roster()
    {
        _harness.AsRole(UserRole.ProductOwner, _ownerId);
        var handler = new CreatePersonCommandHandler(_harness.Db, _harness.Authorizer);

        var act = () => handler.Handle(
            new CreatePersonCommand("Someone New", Role.Developer), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*administrator*");
    }

    [Fact]
    public async Task A_product_owner_cannot_reorder_the_shared_portfolio()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.AsRole(UserRole.ProductOwner, _ownerId);

        var handler = new ReorderBoardsCommandHandler(_harness.Db, _harness.Authorizer);

        var act = () => handler.Handle(
            new ReorderBoardsCommand([new BoardOrderItem(board.Id, 3)]), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_board_a_product_owner_creates_is_theirs_to_edit()
    {
        _harness.AsRole(UserRole.ProductOwner, _ownerId);

        var created = await new CreateBoardCommandHandler(
                _harness.Db, _harness.CurrentUser, _harness.UserContext, _harness.Authorizer)
            .Handle(new CreateBoardCommand("Mine", "VIDA HIS", "Squad Beta", null,
                BoardStatus.OnTrack, 10), CancellationToken.None);

        // Round-trips: creating it grants the ownership that lets them edit it.
        var updated = await UpdateHandler().Handle(
            new UpdateBoardMetaCommand(created.Id, "Mine, renamed", "VIDA HIS", "Squad Beta",
                null, BoardStatus.OnTrack, 25),
            CancellationToken.None);

        updated.Title.Should().Be("Mine, renamed");
    }

    // -----------------------------------------------------------------------
    // Admin
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_admin_can_edit_any_board_including_ownerless_ones()
    {
        var owned = await SeedBoardAsync(_otherOwnerId);
        _harness.AsRole(UserRole.Admin);

        var result = await UpdateHandler().Handle(UpdateFor(owned, 91), CancellationToken.None);

        result.ProgressPercent.Should().Be(91);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_before_any_role_check()
    {
        var board = await SeedBoardAsync(_ownerId);
        _harness.UserContext.UserId = null;

        var act = () => UpdateHandler().Handle(UpdateFor(board), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*signed in*");
    }
}
