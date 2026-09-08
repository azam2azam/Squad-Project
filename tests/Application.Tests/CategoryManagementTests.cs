using Application.Abstractions;
using Application.Categories;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// Categories sit above the boards, so the rules that matter are the ones that stop the
/// grouping quietly rearranging itself: unique names, a soft retire that leaves boards
/// where they are, and a bulk move that is the only practical way to file a portfolio.
/// </summary>
public sealed class CategoryManagementTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private Board SeedBoard(string title = "Discharge Revamp")
    {
        var board = new Board(title, "Discharge", "Squad Aurora", "Q3",
            BoardStatus.OnTrack, 40, "tester");

        _harness.Db.Boards.Add(board);
        _harness.Db.SaveChanges();
        return board;
    }

    private async Task<CategoryDto> CreateAsync(string name, string colour = "#2563EB") =>
        await new CreateCategoryCommandHandler(_harness.Db, _harness.Authorizer)
            .Handle(new CreateCategoryCommand(name, null, colour), default);

    // ------------------------------------------------------------------
    // Creating
    // ------------------------------------------------------------------

    [Fact]
    public async Task Category_names_are_unique()
    {
        await CreateAsync("VIDA 4");

        var act = async () => await CreateAsync("VIDA 4");

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("#FFF")]
    public async Task A_category_colour_must_be_a_full_hex_value(string colour)
    {
        // The colour is rendered straight into a chip, so a bad value paints a broken
        // label rather than failing loudly.
        var act = async () => await CreateAsync("VIDA 4", colour);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*hex*");
    }

    [Fact]
    public async Task Only_an_admin_may_create_a_category()
    {
        _harness.AsRole(UserRole.ProductOwner);

        var act = async () => await CreateAsync("VIDA 4");

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------
    // Boards keep working without one
    // ------------------------------------------------------------------

    [Fact]
    public void A_board_may_have_no_category()
    {
        // Every board that existed before categories did is in this state, so it has to
        // be a valid one rather than a gap to be filled.
        var board = SeedBoard();

        board.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Retiring_a_category_leaves_its_boards_grouped()
    {
        var category = await CreateAsync("VIDA 4");
        var board = SeedBoard();
        board.AssignCategory(category.Id);
        await _harness.Db.SaveChangesAsync();

        await new SetCategoryActiveCommandHandler(_harness.Db, _harness.Authorizer)
            .Handle(new SetCategoryActiveCommand(category.Id, false), default);

        var stored = await _harness.Db.Boards.SingleAsync(b => b.Id == board.Id);

        // Retiring removes it from the pickers. Tipping its boards back into the
        // uncategorised pile would silently rearrange the portfolio.
        stored.CategoryId.Should().Be(category.Id);
    }

    // ------------------------------------------------------------------
    // Bulk assignment
    // ------------------------------------------------------------------

    [Fact]
    public async Task Boards_can_be_filed_in_bulk()
    {
        var category = await CreateAsync("VIDA 4");
        var first = SeedBoard("Discharge");
        var second = SeedBoard("Admission");

        var moved = await new AssignBoardsToCategoryCommandHandler(
                _harness.Db, _harness.Authorizer)
            .Handle(new AssignBoardsToCategoryCommand([first.Id, second.Id], category.Id), default);

        moved.Should().Be(2);
        (await _harness.Db.Boards.CountAsync(b => b.CategoryId == category.Id)).Should().Be(2);
    }

    [Fact]
    public async Task A_null_category_takes_boards_out_of_every_programme()
    {
        var category = await CreateAsync("VIDA 4");
        var board = SeedBoard();
        board.AssignCategory(category.Id);
        await _harness.Db.SaveChangesAsync();

        await new AssignBoardsToCategoryCommandHandler(_harness.Db, _harness.Authorizer)
            .Handle(new AssignBoardsToCategoryCommand([board.Id], null), default);

        (await _harness.Db.Boards.SingleAsync(b => b.Id == board.Id)).CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Filing_into_a_category_that_no_longer_exists_is_refused()
    {
        var board = SeedBoard();

        var act = async () => await new AssignBoardsToCategoryCommandHandler(
                _harness.Db, _harness.Authorizer)
            .Handle(new AssignBoardsToCategoryCommand([board.Id], Guid.NewGuid()), default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*no longer exists*");
    }

    [Fact]
    public async Task Only_an_admin_may_file_boards()
    {
        var category = await CreateAsync("VIDA 4");
        var board = SeedBoard();

        _harness.AsRole(UserRole.ProductOwner);

        var act = async () => await new AssignBoardsToCategoryCommandHandler(
                _harness.Db, _harness.Authorizer)
            .Handle(new AssignBoardsToCategoryCommand([board.Id], category.Id), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------
    // Listing
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_list_reports_how_many_boards_each_category_holds()
    {
        var category = await CreateAsync("VIDA 4");
        var board = SeedBoard();
        board.AssignCategory(category.Id);
        SeedBoard("Unfiled");
        await _harness.Db.SaveChangesAsync();

        var listed = await new ListCategoriesQueryHandler(_harness.Db)
            .Handle(new ListCategoriesQuery(), default);

        // Retiring one is only an informed decision if you can see what is inside it.
        listed.Single().BoardCount.Should().Be(1);
    }

    [Fact]
    public async Task Retired_categories_are_hidden_unless_asked_for()
    {
        var category = await CreateAsync("VIDA 4");

        await new SetCategoryActiveCommandHandler(_harness.Db, _harness.Authorizer)
            .Handle(new SetCategoryActiveCommand(category.Id, false), default);

        var handler = new ListCategoriesQueryHandler(_harness.Db);

        (await handler.Handle(new ListCategoriesQuery(), default)).Should().BeEmpty();
        (await handler.Handle(new ListCategoriesQuery(IncludeInactive: true), default))
            .Should().ContainSingle();
    }
}
