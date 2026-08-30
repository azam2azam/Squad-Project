using Domain.Enums;
using Domain.ValueObjects;
using FluentAssertions;

namespace Domain.Tests;

public class SquadCompositionTests
{
    [Fact]
    public void Empty_squad_reports_no_segments()
    {
        var composition = SquadComposition.From([]);

        composition.Total.Should().Be(0);
        composition.Segments.Should().BeEmpty();
        composition.LegendText.Should().Be("No members yet");
    }

    [Fact]
    public void Counts_are_grouped_by_role()
    {
        var composition = SquadComposition.From([
            Role.Developer, Role.Developer, Role.QaEngineer, Role.ProductOwner
        ]);

        composition.Total.Should().Be(4);
        composition.CountOf(Role.Developer).Should().Be(2);
        composition.CountOf(Role.QaEngineer).Should().Be(1);
        composition.CountOf(Role.TechLead).Should().Be(0);
    }

    [Fact]
    public void Segments_follow_the_canonical_display_order()
    {
        // Deliberately supplied out of order.
        var composition = SquadComposition.From([
            Role.DevOps, Role.Developer, Role.ProductOwner
        ]);

        composition.Segments.Select(s => s.Role)
            .Should().ContainInOrder(Role.ProductOwner, Role.Developer, Role.DevOps);
    }

    [Fact]
    public void Legend_uses_singular_for_one_and_plural_for_many()
    {
        var composition = SquadComposition.From([
            Role.ProductOwner, Role.Developer, Role.Developer
        ]);

        composition.LegendText.Should().Be("1 Product Owner · 2 Developers");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public void Percentages_always_sum_to_exactly_one_hundred(int memberCount)
    {
        // Three roles at these counts produce repeating decimals; the bar must still fill.
        var roles = Enumerable.Range(0, memberCount)
            .Select(i => (Role)(i % 3))
            .ToList();

        var composition = SquadComposition.From(roles);

        composition.Segments.Sum(s => s.Percent).Should().BeApproximately(100d, 0.0001);
    }

    [Fact]
    public void Segments_carry_the_role_design_tokens()
    {
        var composition = SquadComposition.From([Role.QaEngineer]);

        var segment = composition.Segments.Single();
        segment.Label.Should().Be("QA Engineer");
        segment.Color.Should().Be("#F59E0B");
        segment.Percent.Should().Be(100d);
    }
}
