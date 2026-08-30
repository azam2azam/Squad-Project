using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;

namespace Domain.Tests;

public class BoardTests
{
    private static Board NewBoard() =>
        new("OPD Screen Revamp", "VIDA HIS", "Squad Alpha", "Sprint 14",
            BoardStatus.OnTrack, 68, "tester");

    private static Person NewPerson(string name = "Huda Rahman", Role role = Role.Developer) =>
        new(name, role, "Angular · Signals");

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_outside_zero_to_one_hundred_is_rejected(int progress)
    {
        var act = () => new Board("T", "P", "S", null, BoardStatus.OnTrack, progress, "tester");

        act.Should().Throw<DomainException>()
            .WithMessage("Progress must be between 0 and 100.");
    }

    [Fact]
    public void Title_product_and_squad_are_required()
    {
        var act = () => new Board("  ", "VIDA HIS", "Squad Alpha", null,
            BoardStatus.OnTrack, 10, "tester");

        act.Should().Throw<DomainException>().WithMessage("Board requires a title.");
    }

    [Fact]
    public void Adding_the_same_person_twice_is_rejected()
    {
        var board = NewBoard();
        var person = NewPerson();
        board.AddMember(person, Role.Developer);

        var act = () => board.AddMember(person, Role.TechLead);

        act.Should().Throw<DomainException>().WithMessage("*already on this squad*");
    }

    [Fact]
    public void Inactive_people_cannot_be_added_to_a_squad()
    {
        var board = NewBoard();
        var person = NewPerson();
        person.Deactivate();

        var act = () => board.AddMember(person, Role.Developer);

        act.Should().Throw<DomainException>().WithMessage("*not an active roster member*");
    }

    [Fact]
    public void Member_role_may_differ_from_the_person_default()
    {
        var board = NewBoard();
        var person = NewPerson("Faisal Al-Qahtani", Role.TechLead);

        var member = board.AddMember(person, Role.Developer);

        member.Role.Should().Be(Role.Developer);
        person.DefaultRole.Should().Be(Role.TechLead);
    }

    [Fact]
    public void Member_detail_falls_back_to_the_person_default()
    {
        var board = NewBoard();
        var member = board.AddMember(NewPerson(), Role.Developer);

        member.Detail.Should().Be("Angular · Signals");
    }

    [Fact]
    public void Missing_product_owner_and_developers_surface_as_warnings_not_errors()
    {
        var board = NewBoard();
        board.AddMember(NewPerson("Layla Mansour", Role.QaEngineer), Role.QaEngineer);

        board.Warnings.Should().Contain("This squad has no Product Owner.");
        board.Warnings.Should().Contain("This squad has no Developers.");
    }

    [Fact]
    public void A_complete_squad_raises_no_warnings()
    {
        var board = NewBoard();
        board.AddMember(NewPerson("Nadia Al-Harbi", Role.ProductOwner), Role.ProductOwner);
        board.AddMember(NewPerson("Huda Rahman", Role.Developer), Role.Developer);

        board.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Blocked_without_a_blocker_note_is_flagged()
    {
        var board = NewBoard();
        board.AddMember(NewPerson("Nadia Al-Harbi", Role.ProductOwner), Role.ProductOwner);
        board.AddMember(NewPerson("Huda Rahman", Role.Developer), Role.Developer);

        board.UpdateMeta(board.Title, board.Product, board.SquadName, board.Sprint,
            BoardStatus.Blocked, board.ProgressPercent, blockerNote: null,
            velocity: null, targetDate: null, jiraProjectKey: null, jiraBoardId: null);

        board.Warnings.Should().ContainSingle()
            .Which.Should().Contain("no blocker note");
    }

    [Fact]
    public void Removing_a_member_resequences_the_remaining_order()
    {
        var board = NewBoard();
        var first = board.AddMember(NewPerson("A One", Role.Developer), Role.Developer);
        var second = board.AddMember(NewPerson("B Two", Role.Developer), Role.Developer);
        var third = board.AddMember(NewPerson("C Three", Role.QaEngineer), Role.QaEngineer);

        board.RemoveMember(second.Id);

        board.Members.Should().HaveCount(2);
        first.OrderIndex.Should().Be(0);
        third.OrderIndex.Should().Be(1);
    }

    [Fact]
    public void Duplicate_copies_membership_onto_a_new_board_identity()
    {
        var board = NewBoard();
        board.AddMember(NewPerson("Nadia Al-Harbi", Role.ProductOwner), Role.ProductOwner);
        board.AddMember(NewPerson("Huda Rahman", Role.Developer), Role.Developer);

        var copy = board.Duplicate("tester");

        copy.Id.Should().NotBe(board.Id);
        copy.Title.Should().Be("OPD Screen Revamp (copy)");
        copy.Members.Should().HaveCount(2);
        copy.Members.Select(m => m.BoardId).Should().AllBeEquivalentTo(copy.Id);
        // The roster entries themselves are shared, not cloned.
        copy.Members.Select(m => m.PersonId)
            .Should().BeEquivalentTo(board.Members.Select(m => m.PersonId));
    }

    [Fact]
    public void Soft_delete_is_idempotent_and_reversible()
    {
        var board = NewBoard();

        board.SoftDelete();
        board.SoftDelete();
        board.IsDeleted.Should().BeTrue();

        board.Restore();
        board.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Reordering_places_unlisted_members_after_the_listed_ones()
    {
        var board = NewBoard();
        var a = board.AddMember(NewPerson("A One", Role.Developer), Role.Developer);
        var b = board.AddMember(NewPerson("B Two", Role.Developer), Role.Developer);
        var c = board.AddMember(NewPerson("C Three", Role.QaEngineer), Role.QaEngineer);

        board.ReorderMembers([c.Id, a.Id]);

        c.OrderIndex.Should().Be(0);
        a.OrderIndex.Should().Be(1);
        b.OrderIndex.Should().Be(2);
    }
}
