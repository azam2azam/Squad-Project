using Application.Members.Commands;
using Application.People.Commands;
using Application.People.Queries;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

public class MemberHandlerTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<(Board Board, Person Po, Person Dev)> SeedAsync()
    {
        var board = new Board("OPD Screen Revamp", "VIDA HIS", "Squad Alpha", "Sprint 14",
            BoardStatus.OnTrack, 68, "seed");

        var po = new Person("Nadia Al-Harbi", Role.ProductOwner, "Outpatient journey");
        var dev = new Person("Huda Rahman", Role.Developer, "Angular · Signals");
        _harness.Db.People.AddRange(po, dev);

        board.AddMember(po, Role.ProductOwner);
        _harness.Db.Boards.Add(board);
        await _harness.Db.SaveChangesAsync();

        return (board, po, dev);
    }

    // -----------------------------------------------------------------------
    // Add
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Adds_an_existing_roster_person_to_the_squad()
    {
        var (board, _, dev) = await SeedAsync();
        var handler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        var result = await handler.Handle(
            new AddMemberCommand(board.Id, dev.Id, null, Role.Developer, "Angular"),
            CancellationToken.None);

        result.FullName.Should().Be("Huda Rahman");
        result.RoleLabel.Should().Be("Developer");
        result.RoleColor.Should().Be("#6366F1");
        result.Detail.Should().Be("Angular");

        var saved = await _harness.Db.SquadMembers.CountAsync();
        saved.Should().Be(2);
    }

    [Fact]
    public async Task Quick_creating_a_person_inline_also_adds_them_to_the_roster()
    {
        var (board, _, _) = await SeedAsync();
        var handler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        await handler.Handle(
            new AddMemberCommand(board.Id, null,
                new NewPersonInput("Tariq Nawaz", Role.DevOps, "Kubernetes"),
                Role.DevOps),
            CancellationToken.None);

        // The point of roster-first membership: the name is reusable next time.
        var roster = await new ListPeopleQueryHandler(_harness.Db)
            .Handle(new ListPeopleQuery(Search: "Tariq"), CancellationToken.None);

        roster.Items.Should().ContainSingle().Which.FullName.Should().Be("Tariq Nawaz");
    }

    [Fact]
    public async Task The_same_person_cannot_be_added_to_a_squad_twice()
    {
        var (board, po, _) = await SeedAsync();
        var handler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        var act = () => handler.Handle(
            new AddMemberCommand(board.Id, po.Id, null, Role.TechLead), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already on this squad*");
    }

    [Fact]
    public async Task A_deactivated_person_cannot_be_added_to_a_squad()
    {
        var (board, _, dev) = await SeedAsync();
        dev.Deactivate();
        await _harness.Db.SaveChangesAsync();

        var handler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        var act = () => handler.Handle(
            new AddMemberCommand(board.Id, dev.Id, null, Role.Developer), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not an active roster member*");
    }

    [Fact]
    public async Task Adding_a_member_broadcasts_the_change()
    {
        var (board, _, dev) = await SeedAsync();
        var handler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        await handler.Handle(
            new AddMemberCommand(board.Id, dev.Id, null, Role.Developer), CancellationToken.None);

        _harness.Notifier.MemberChanges.Should().ContainSingle()
            .Which.BoardId.Should().Be(board.Id);
    }

    // -----------------------------------------------------------------------
    // Update / remove / reorder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_members_role_can_differ_from_their_roster_default()
    {
        var (board, _, dev) = await SeedAsync();
        var added = await new AddMemberCommandHandler(_harness.Db, _harness.Notifier)
            .Handle(new AddMemberCommand(board.Id, dev.Id, null, Role.Developer),
                CancellationToken.None);

        var updated = await new UpdateMemberCommandHandler(_harness.Db, _harness.Notifier)
            .Handle(new UpdateMemberCommand(added.Id, Role.TechLead, "Architecture", 60),
                CancellationToken.None);

        updated.RoleLabel.Should().Be("Tech Lead");
        updated.AllocationPercent.Should().Be(60);

        // The roster default is untouched.
        var person = await _harness.Db.People.SingleAsync(p => p.Id == dev.Id);
        person.DefaultRole.Should().Be(Role.Developer);
    }

    [Fact]
    public async Task Removing_a_member_resequences_the_rest()
    {
        var (board, _, dev) = await SeedAsync();
        var addHandler = new AddMemberCommandHandler(_harness.Db, _harness.Notifier);

        var second = await addHandler.Handle(
            new AddMemberCommand(board.Id, dev.Id, null, Role.Developer), CancellationToken.None);
        var third = await addHandler.Handle(
            new AddMemberCommand(board.Id, null, new NewPersonInput("Layla Mansour", Role.QaEngineer),
                Role.QaEngineer),
            CancellationToken.None);

        await new RemoveMemberCommandHandler(_harness.Db, _harness.Notifier)
            .Handle(new RemoveMemberCommand(second.Id), CancellationToken.None);

        var remaining = await _harness.Db.SquadMembers.OrderBy(m => m.OrderIndex).ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Select(m => m.OrderIndex).Should().ContainInOrder(0, 1);
        remaining.Should().Contain(m => m.Id == third.Id);
    }

    [Fact]
    public async Task Removing_a_member_leaves_the_person_on_the_roster()
    {
        var (board, _, dev) = await SeedAsync();
        var added = await new AddMemberCommandHandler(_harness.Db, _harness.Notifier)
            .Handle(new AddMemberCommand(board.Id, dev.Id, null, Role.Developer),
                CancellationToken.None);

        await new RemoveMemberCommandHandler(_harness.Db, _harness.Notifier)
            .Handle(new RemoveMemberCommand(added.Id), CancellationToken.None);

        (await _harness.Db.People.CountAsync(p => p.Id == dev.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Reorder_rejects_a_member_that_is_not_on_the_board()
    {
        var (board, _, _) = await SeedAsync();
        var handler = new ReorderMembersCommandHandler(_harness.Db, _harness.Notifier);

        var act = () => handler.Handle(
            new ReorderMembersCommand(board.Id, [Guid.NewGuid()]), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // -----------------------------------------------------------------------
    // Roster
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Deactivated_people_are_hidden_from_the_picker_but_keep_their_assignments()
    {
        var (board, po, _) = await SeedAsync();

        await new DeactivatePersonCommandHandler(_harness.Db)
            .Handle(new DeactivatePersonCommand(po.Id), CancellationToken.None);

        var picker = await new ListPeopleQueryHandler(_harness.Db)
            .Handle(new ListPeopleQuery(), CancellationToken.None);
        picker.Items.Should().NotContain(p => p.Id == po.Id);

        var manager = await new ListPeopleQueryHandler(_harness.Db)
            .Handle(new ListPeopleQuery(IncludeInactive: true), CancellationToken.None);
        manager.Items.Should().Contain(p => p.Id == po.Id && !p.IsActive);

        // The historical assignment survives (spec section 5).
        (await _harness.Db.SquadMembers.CountAsync(m => m.PersonId == po.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Roster_search_matches_name_email_and_skills()
    {
        await SeedAsync();
        var create = new CreatePersonCommandHandler(_harness.Db);
        await create.Handle(
            new CreatePersonCommand("Tariq Nawaz", Role.DevOps, "Kubernetes · Azure",
                "tariq@example.com"),
            CancellationToken.None);

        var handler = new ListPeopleQueryHandler(_harness.Db);

        (await handler.Handle(new ListPeopleQuery("Kubernetes"), CancellationToken.None))
            .Items.Should().ContainSingle().Which.FullName.Should().Be("Tariq Nawaz");

        (await handler.Handle(new ListPeopleQuery("tariq@"), CancellationToken.None))
            .Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Reactivating_puts_a_person_back_in_the_picker()
    {
        var (_, po, _) = await SeedAsync();
        await new DeactivatePersonCommandHandler(_harness.Db)
            .Handle(new DeactivatePersonCommand(po.Id), CancellationToken.None);

        var result = await new ReactivatePersonCommandHandler(_harness.Db)
            .Handle(new ReactivatePersonCommand(po.Id), CancellationToken.None);

        result.IsActive.Should().BeTrue();
    }
}
