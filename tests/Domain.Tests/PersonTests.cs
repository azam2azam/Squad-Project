using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;

namespace Domain.Tests;

public class PersonTests
{
    [Theory]
    [InlineData("Nadia Al-Harbi", "NA")]
    [InlineData("Huda Rahman", "HR")]
    [InlineData("Mohammed bin Rashid Al Maktoum", "MM")]
    [InlineData("Cher", "CH")]
    [InlineData("X", "X")]
    [InlineData("  Layla   Mansour  ", "LM")]
    public void Initials_take_the_first_and_last_name(string fullName, string expected)
    {
        Person.ComputeInitials(fullName).Should().Be(expected);
    }

    [Fact]
    public void A_person_requires_a_name()
    {
        var act = () => new Person("   ", Role.Developer);

        act.Should().Throw<DomainException>().WithMessage("Person requires a full name.");
    }

    [Fact]
    public void Blank_optional_fields_are_normalised_to_null()
    {
        var person = new Person("Omar Siddiqui", Role.Developer,
            defaultDetail: "   ", email: "", avatarColorOverride: null);

        person.DefaultDetail.Should().BeNull();
        person.Email.Should().BeNull();
        person.AvatarColorOverride.Should().BeNull();
    }

    [Fact]
    public void Deactivation_is_a_soft_delete_that_can_be_undone()
    {
        var person = new Person("Tariq Nawaz", Role.DevOps);

        person.IsActive.Should().BeTrue();

        person.Deactivate();
        person.IsActive.Should().BeFalse();

        person.Reactivate();
        person.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivating_a_person_leaves_their_squad_assignments_intact()
    {
        var board = new Board("OPD Screen Revamp", "VIDA HIS", "Squad Alpha", "Sprint 14",
            BoardStatus.OnTrack, 68, "tester");
        var person = new Person("Huda Rahman", Role.Developer);
        board.AddMember(person, Role.Developer);

        person.Deactivate();

        // The historical record must survive the roster change (spec section 5).
        board.Members.Should().ContainSingle()
            .Which.PersonId.Should().Be(person.Id);
    }
}
