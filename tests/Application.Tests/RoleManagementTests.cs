using Application.Abstractions;
using Application.Roles;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// Roles became configurable without migrating the numbers already stored on every member
/// and person. These pin the things that would break quietly if that went wrong: the
/// built-ins keeping their values, custom roles rendering rather than throwing, and a
/// retired role still showing correctly wherever it is already assigned.
/// </summary>
public sealed class RoleManagementTests : IDisposable
{
    private readonly TestHarness _harness = new();

    // The built-in seven arrive with the schema: SquadRoleConfiguration seeds them with
    // HasData, so EnsureCreated puts them in every harness database. Seeding them again
    // here would violate the primary key.

    public void Dispose()
    {
        // The catalogue is process-wide, so a test that configures it must not leak into
        // the next one.
        RoleMetadata.Reset();
        _harness.Dispose();
    }

    private sealed class NoopCatalog : IRoleCatalog
    {
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // The built-ins
    // ------------------------------------------------------------------

    [Fact]
    public void The_built_in_roles_keep_the_numbers_already_stored_on_every_member()
    {
        // If these ever shift, existing boards silently change everyone's role.
        _harness.Db.SquadRoles.Single(r => r.Name == "ProductOwner").Value.Should().Be(0);
        _harness.Db.SquadRoles.Single(r => r.Name == "Developer").Value.Should().Be(2);
        _harness.Db.SquadRoles.Single(r => r.Name == "DevOps").Value.Should().Be(6);
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_retired()
    {
        var handler = new SetRoleActiveCommandHandler(
            _harness.Db, _harness.Authorizer, new NoopCatalog());

        var act = async () => await handler.Handle(
            new SetRoleActiveCommand((int)Role.Developer, false), default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*built-in*");
    }

    [Fact]
    public async Task A_built_in_role_can_be_renamed_and_recoloured()
    {
        // An org that calls it "Delivery Lead" should be able to say so.
        var handler = new UpdateRoleCommandHandler(
            _harness.Db, _harness.Authorizer, new NoopCatalog());

        await handler.Handle(
            new UpdateRoleCommand((int)Role.TechLead, "Delivery Lead", "Delivery Leads",
                "#FF8800", 1),
            default);

        var stored = await _harness.Db.SquadRoles.SingleAsync(r => r.Value == (int)Role.TechLead);

        stored.Label.Should().Be("Delivery Lead");
        stored.Color.Should().Be("#FF8800");
        // The identifier is what spreadsheets match on, so it does not move.
        stored.Name.Should().Be("TechLead");
    }

    // ------------------------------------------------------------------
    // Custom roles
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_custom_role_is_numbered_clear_of_the_built_ins()
    {
        var created = await CreateAsync("ScrumMaster", "Scrum Master", "#F472B6");

        created.Value.Should().BeGreaterThanOrEqualTo(RoleMetadata.FirstCustomValue);
        created.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task Role_numbers_are_never_reused()
    {
        var first = await CreateAsync("ScrumMaster", "Scrum Master", "#F472B6");

        // Retire it, then add another: the new one must not inherit the retired number,
        // or people holding the old role would silently become the new one.
        await new SetRoleActiveCommandHandler(_harness.Db, _harness.Authorizer, new NoopCatalog())
            .Handle(new SetRoleActiveCommand(first.Value, false), default);

        var second = await CreateAsync("DataEngineer", "Data Engineer", "#22D3EE");

        second.Value.Should().BeGreaterThan(first.Value);
    }

    [Fact]
    public async Task Duplicate_identifiers_and_labels_are_refused()
    {
        await CreateAsync("ScrumMaster", "Scrum Master", "#F472B6");

        var sameName = async () => await CreateAsync("ScrumMaster", "Something Else", "#22D3EE");
        await sameName.Should().ThrowAsync<DomainException>().WithMessage("*already exists*");

        var sameLabel = async () => await CreateAsync("Другой", "Scrum Master", "#22D3EE");
        await sameLabel.Should().ThrowAsync<DomainException>();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("F472B6")]
    public async Task A_role_colour_must_be_a_full_hex_value(string colour)
    {
        // The colour is rendered straight into the slide, so a bad value would paint a
        // broken avatar rather than fail loudly.
        var act = async () => await CreateAsync("ScrumMaster", "Scrum Master", colour);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*hex*");
    }

    [Theory]
    [InlineData("Scrum Master")]
    [InlineData("2Fast")]
    [InlineData("Scrum-Master")]
    public async Task A_role_identifier_must_be_a_plain_word(string name)
    {
        var act = async () => await CreateAsync(name, "Scrum Master", "#F472B6");

        await act.Should().ThrowAsync<DomainException>().WithMessage("*identifier*");
    }

    [Fact]
    public async Task Only_an_admin_may_add_a_role()
    {
        _harness.AsRole(UserRole.ProductOwner);

        var act = async () => await CreateAsync("ScrumMaster", "Scrum Master", "#F472B6");

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------

    [Fact]
    public void A_configured_custom_role_renders_with_its_own_label_and_colour()
    {
        RoleMetadata.Configure(
        [
            .. RoleMetadata.Defaults,
            new RoleDefinition((Role)100, "ScrumMaster", "Scrum Master", "Scrum Masters",
                "#F472B6", 100)
        ]);

        RoleMetadata.Label((Role)100).Should().Be("Scrum Master");
        RoleMetadata.Color((Role)100).Should().Be("#F472B6");
        RoleMetadata.CountLabel((Role)100, 2).Should().Be("Scrum Masters");
    }

    [Fact]
    public void An_unknown_role_renders_as_a_placeholder_rather_than_throwing()
    {
        // Happens when a role exists on another instance that has not refreshed here.
        // A missing label must not take down a board.
        var act = () => RoleMetadata.Label((Role)999);

        act.Should().NotThrow();
        RoleMetadata.Label((Role)999).Should().Be("Role 999");
        RoleMetadata.IsKnown((Role)999).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_role_still_appears_on_the_composition_bar()
    {
        // Otherwise the segments would not add up to 100% and a member would vanish
        // from the slide without explanation.
        var composition = SquadComposition.From([Role.Developer, (Role)999]);

        composition.Total.Should().Be(2);
        composition.Segments.Should().HaveCount(2);
        composition.Segments.Sum(s => s.Percent).Should().BeApproximately(100d, 0.0001);
    }

    [Fact]
    public void A_retired_role_still_renders_for_people_who_already_hold_it()
    {
        // The catalogue keeps inactive roles on purpose: retiring one removes it from the
        // pickers, it does not blank out historical boards.
        RoleMetadata.Configure(
        [
            .. RoleMetadata.Defaults,
            new RoleDefinition((Role)100, "ScrumMaster", "Scrum Master", "Scrum Masters",
                "#F472B6", 100)
        ]);

        var composition = SquadComposition.From([(Role)100]);

        composition.LegendText.Should().Be("1 Scrum Master");
        composition.Segments.Single().Color.Should().Be("#F472B6");
    }

    [Fact]
    public void An_empty_catalogue_is_refused_in_favour_of_the_built_ins()
    {
        // Configuring nothing would blank every avatar and legend in the app, and the
        // cause would be invisible.
        RoleMetadata.Configure([]);

        RoleMetadata.DisplayOrder.Should().HaveCount(RoleMetadata.Defaults.Count);
        RoleMetadata.Label(Role.Developer).Should().Be("Developer");
    }

    private async Task<SquadRoleDto> CreateAsync(string name, string label, string colour) =>
        await new CreateRoleCommandHandler(_harness.Db, _harness.Authorizer, new NoopCatalog())
            .Handle(new CreateRoleCommand(name, label, null, colour), default);
}
