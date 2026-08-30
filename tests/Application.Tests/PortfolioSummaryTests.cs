using Application.Portfolio;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;

namespace Application.Tests;

public class PortfolioSummaryTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private Board AddBoard(string title, string squad, BoardStatus status, int progress,
        RiskLevel risk = RiskLevel.None, string? riskNote = null)
    {
        var board = new Board(title, "VIDA HIS", squad, "Sprint 1", status, progress, "seed");
        board.UpdateMeta(title, "VIDA HIS", squad, "Sprint 1", status, progress,
            status == BoardStatus.Blocked ? "Blocked on a vendor" : null,
            null, null, null, null, risk, riskNote);

        _harness.Db.Boards.Add(board);
        return board;
    }

    private Task<PortfolioSummaryDto> SummariseAsync() =>
        new GetPortfolioSummaryQueryHandler(_harness.Db)
            .Handle(new GetPortfolioSummaryQuery(), CancellationToken.None);

    [Fact]
    public async Task An_empty_portfolio_summarises_without_dividing_by_zero()
    {
        var summary = await SummariseAsync();

        summary.Headline.TotalBoards.Should().Be(0);
        summary.Headline.AverageProgressPercent.Should().Be(0);
        summary.Headline.OnTrackPercent.Should().Be(0);
        summary.StatusBreakdown.Should().BeEmpty();
        summary.Squads.Should().BeEmpty();
    }

    [Fact]
    public async Task Headline_counts_delivered_boards_as_on_track()
    {
        AddBoard("A", "Alpha", BoardStatus.OnTrack, 50);
        AddBoard("B", "Alpha", BoardStatus.Delivered, 100);
        AddBoard("C", "Beta", BoardStatus.Blocked, 10);
        AddBoard("D", "Beta", BoardStatus.AtRisk, 40);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        summary.Headline.TotalBoards.Should().Be(4);
        summary.Headline.SquadCount.Should().Be(2);
        summary.Headline.AverageProgressPercent.Should().Be(50);
        // Delivered work is not "off track" — 2 of 4.
        summary.Headline.OnTrackPercent.Should().Be(50);
    }

    [Fact]
    public async Task Delivered_is_flagged_for_texture_because_it_shares_a_hue_with_on_track()
    {
        AddBoard("A", "Alpha", BoardStatus.OnTrack, 50);
        AddBoard("B", "Alpha", BoardStatus.Delivered, 100);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        // #34D399 vs #2DD4BF measure ΔE 5.2 — under the readable floor — so a chart
        // must not rely on colour alone to tell them apart.
        summary.StatusBreakdown.Single(s => s.Status == BoardStatus.Delivered)
            .NeedsTexture.Should().BeTrue();
        summary.StatusBreakdown.Single(s => s.Status == BoardStatus.OnTrack)
            .NeedsTexture.Should().BeFalse();
    }

    [Fact]
    public async Task Status_breakdown_omits_statuses_with_no_boards()
    {
        AddBoard("A", "Alpha", BoardStatus.OnTrack, 50);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        summary.StatusBreakdown.Should().ContainSingle();
        summary.StatusBreakdown[0].Percent.Should().Be(100);
    }

    [Fact]
    public async Task Squads_are_ordered_worst_progress_first()
    {
        AddBoard("A", "Ahead", BoardStatus.OnTrack, 90);
        AddBoard("B", "Behind", BoardStatus.AtRisk, 20);
        AddBoard("C", "Middling", BoardStatus.OnTrack, 55);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        // The dashboard should lead with what needs asking about.
        summary.Squads.Select(s => s.SquadName)
            .Should().ContainInOrder("Behind", "Middling", "Ahead");
    }

    [Fact]
    public async Task A_person_on_two_boards_of_one_squad_counts_once()
    {
        var person = new Person("Huda Rahman", Role.Developer);
        _harness.Db.People.Add(person);

        var first = AddBoard("A", "Alpha", BoardStatus.OnTrack, 50);
        var second = AddBoard("B", "Alpha", BoardStatus.OnTrack, 70);
        first.AddMember(person, Role.Developer);
        second.AddMember(person, Role.Developer);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        summary.Squads.Single().MemberCount.Should().Be(1);
        // But the role totals count assignments, which is the useful number there.
        summary.RoleTotals.Single(r => r.Role == Role.Developer).Count.Should().Be(2);
    }

    [Fact]
    public async Task The_risk_register_includes_blocked_boards_even_with_no_risk_set()
    {
        AddBoard("Blocked but no risk", "Alpha", BoardStatus.Blocked, 10);
        AddBoard("Healthy", "Alpha", BoardStatus.OnTrack, 80);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        var entry = summary.RiskRegister.Should().ContainSingle().Subject;
        entry.Title.Should().Be("Blocked but no risk");
        // Falls back to the blocker note, which is the useful text in that case.
        entry.RiskNote.Should().Be("Blocked on a vendor");
    }

    [Fact]
    public async Task The_risk_register_is_worst_first_and_ignores_low_risk()
    {
        AddBoard("Low", "Alpha", BoardStatus.OnTrack, 50, RiskLevel.Low, "Minor");
        AddBoard("Critical", "Alpha", BoardStatus.OnTrack, 50, RiskLevel.Critical, "Serious");
        AddBoard("Medium", "Alpha", BoardStatus.OnTrack, 50, RiskLevel.Medium, "Some");
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        summary.RiskRegister.Select(r => r.Title)
            .Should().ContainInOrder("Critical", "Medium");
        summary.RiskRegister.Should().NotContain(r => r.Title == "Low");
    }

    [Fact]
    public async Task The_attention_count_and_the_attention_list_cannot_disagree()
    {
        AddBoard("Blocked", "Alpha", BoardStatus.Blocked, 10);
        AddBoard("Risky", "Alpha", BoardStatus.OnTrack, 50, RiskLevel.High, "Something");
        AddBoard("Fine but memberless", "Alpha", BoardStatus.OnTrack, 80);
        await _harness.Db.SaveChangesAsync();

        var summary = await SummariseAsync();

        // Same predicate feeds both, so the headline can never contradict the list.
        summary.Headline.BoardsNeedingAttention.Should().Be(summary.NeedsAttention.Count);
    }

    [Fact]
    public async Task A_notable_risk_without_a_note_is_flagged_as_a_warning()
    {
        var board = AddBoard("Risky", "Alpha", BoardStatus.OnTrack, 50, RiskLevel.High);
        await _harness.Db.SaveChangesAsync();

        board.Warnings.Should().Contain(w => w.Contains("High") && w.Contains("no risk note"));

        var summary = await SummariseAsync();
        summary.NeedsAttention.Should().ContainSingle()
            .Which.Reasons.Should().Contain(r => r.Contains("High risk"));
    }
}
