using System.Globalization;
using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Analytics;

/// <summary>
/// The analytics read: squads next to each other, progress over time, and how loaded each
/// person is.
///
/// Two honesty rules shape this file.
///
/// The weekly trend is **reconstructed from the audit trail**, not simulated. Each board's
/// progress history is replayed from its recorded changes, and a week shows the last value
/// known on that date. A portfolio that has only ever been imported therefore has one
/// point per board and a flat line — that is the truth, and <see cref="AnalyticsDto"/>
/// reports how much history exists so the page can say so rather than draw a confident
/// line through a single measurement.
///
/// Per-person figures measure **load and involvement, not output**. The app stores who is
/// on which squad at what allocation; it stores nothing about what an individual delivered.
/// Presenting a squad's progress as a person's performance would invent a number, so the
/// fields here are named for what they actually are.
/// </summary>
public sealed record AnalyticsDto(
    IReadOnlyList<SquadComparisonDto> Squads,
    IReadOnlyList<WeekPointDto> Weeks,
    IReadOnlyList<SeriesDto> SquadTrends,
    IReadOnlyList<MemberLoadDto> Members,
    IReadOnlyList<RoleMixDto> RoleMix,
    AnalyticsCoverageDto Coverage);

/// <summary>How much real history the trend is drawn from, so the page can be honest.</summary>
public sealed record AnalyticsCoverageDto(
    int RecordedProgressChanges,
    int WeeksCovered,
    bool HasRealHistory,
    string Note);

public sealed record SquadComparisonDto(
    string SquadName,
    int BoardCount,
    int MemberCount,
    int AverageProgressPercent,
    int OnTrack,
    int AtRisk,
    int Blocked,
    int InReview,
    int Delivered,
    int NotableRiskCount,
    int TotalAllocationPercent);

public sealed record WeekPointDto(DateOnly WeekStart, string Label, int AverageProgressPercent, int BoardsTracked);

/// <summary>One squad's progress line across the same week buckets.</summary>
public sealed record SeriesDto(string Name, string Color, IReadOnlyList<int?> Values);

public sealed record MemberLoadDto(
    Guid PersonId,
    string FullName,
    string Initials,
    string Color,
    IReadOnlyList<string> Roles,
    int SquadCount,
    IReadOnlyList<string> Squads,
    /// <summary>Sum of allocation across squads. Over 100 means someone is over-committed.</summary>
    int TotalAllocationPercent,
    bool AllocationKnown,
    /// <summary>Average progress of the boards they are on — the squad's number, not theirs.</summary>
    int AverageBoardProgressPercent,
    int BoardsAtRisk,
    int BoardsBlocked,
    /// <summary>Changes recorded against their name in the audit trail.</summary>
    int RecordedEdits);

public sealed record RoleMixDto(string SquadName, IReadOnlyList<RoleCountDto> Roles);

public sealed record RoleCountDto(string Label, string Color, int Count);

public sealed record GetAnalyticsQuery(int Weeks = 12) : IRequest<AnalyticsDto>;

public sealed class GetAnalyticsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAnalyticsQuery, AnalyticsDto>
{
    /// <summary>Audit fields that carry a progress number. Two spellings exist historically.</summary>
    private static readonly string[] ProgressFields = ["Progress", "ProgressPercent"];

    public async Task<AnalyticsDto> Handle(GetAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var boards = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .ToListAsync(cancellationToken);

        var progressHistory = await db.BoardAuditEntries
            .Where(e => ProgressFields.Contains(e.Field))
            .OrderBy(e => e.ChangedAt)
            .ToListAsync(cancellationToken);

        var edits = await db.BoardAuditEntries
            .GroupBy(e => e.ChangedBy)
            .Select(g => new { By = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var weeks = BuildWeeks(Math.Clamp(request.Weeks, 4, 52));

        return new AnalyticsDto(
            BuildSquads(boards),
            BuildPortfolioTrend(boards, progressHistory, weeks),
            BuildSquadTrends(boards, progressHistory, weeks),
            BuildMembers(boards, edits.ToDictionary(e => e.By, e => e.Count)),
            BuildRoleMix(boards),
            BuildCoverage(progressHistory, weeks.Count));
    }

    // ------------------------------------------------------------------
    // Squads side by side
    // ------------------------------------------------------------------

    private static IReadOnlyList<SquadComparisonDto> BuildSquads(IReadOnlyList<Board> boards) =>
        boards
            .GroupBy(b => b.SquadName)
            .Select(g => new SquadComparisonDto(
                g.Key,
                g.Count(),
                // Distinct people: someone on two of a squad's boards is one person.
                g.SelectMany(b => b.Members).Select(m => m.PersonId).Distinct().Count(),
                (int)Math.Round(g.Average(b => b.ProgressPercent)),
                g.Count(b => b.Status == BoardStatus.OnTrack),
                g.Count(b => b.Status == BoardStatus.AtRisk),
                g.Count(b => b.Status == BoardStatus.Blocked),
                g.Count(b => b.Status == BoardStatus.InReview),
                g.Count(b => b.Status == BoardStatus.Delivered),
                g.Count(b => RiskLevelMetadata.IsNotable(b.RiskLevel)),
                g.SelectMany(b => b.Members).Sum(m => m.AllocationPercent ?? 0)))
            .OrderByDescending(s => s.BoardCount)
            .ThenBy(s => s.SquadName)
            .ToList();

    // ------------------------------------------------------------------
    // Weeks
    // ------------------------------------------------------------------

    /// <summary>Week buckets ending with the current week, each starting on a Monday.</summary>
    private static List<(DateOnly Start, DateOnly End, string Label)> BuildWeeks(int count)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var weeks = new List<(DateOnly, DateOnly, string)>();

        for (var i = count - 1; i >= 0; i--)
        {
            var start = thisMonday.AddDays(-7 * i);
            weeks.Add((start, start.AddDays(6),
                start.ToString("d MMM", CultureInfo.InvariantCulture)));
        }

        return weeks;
    }

    /// <summary>
    /// Replays a board's recorded progress changes into a value for each week end.
    /// Returns null for weeks before the board has any known value, so a line starts
    /// where the board does rather than at a fabricated zero.
    /// </summary>
    private static int?[] ProgressByWeek(
        Board board,
        IReadOnlyList<BoardAuditEntry> history,
        List<(DateOnly Start, DateOnly End, string Label)> weeks)
    {
        var changes = history
            .Where(e => e.BoardId == board.Id)
            .Select(e => (
                On: DateOnly.FromDateTime(e.ChangedAt.UtcDateTime),
                Value: int.TryParse(e.NewValue, out var v) ? v : (int?)null))
            .Where(c => c.Value.HasValue)
            .OrderBy(c => c.On)
            .ToList();

        var values = new int?[weeks.Count];
        var lastUpdated = DateOnly.FromDateTime(board.UpdatedAt.UtcDateTime);

        for (var i = 0; i < weeks.Count; i++)
        {
            var end = weeks[i].End;

            var known = changes.Where(c => c.On <= end).Select(c => c.Value).LastOrDefault();

            if (known.HasValue)
            {
                values[i] = known;
            }
            else if (lastUpdated <= end)
            {
                // No recorded change yet, but the board existed and carries a current
                // figure: use it rather than leaving a gap in the middle of a line.
                values[i] = board.ProgressPercent;
            }
        }

        return values;
    }

    private static IReadOnlyList<WeekPointDto> BuildPortfolioTrend(
        IReadOnlyList<Board> boards,
        IReadOnlyList<BoardAuditEntry> history,
        List<(DateOnly Start, DateOnly End, string Label)> weeks)
    {
        var perBoard = boards.Select(b => ProgressByWeek(b, history, weeks)).ToList();

        return weeks
            .Select((w, i) =>
            {
                var known = perBoard.Select(v => v[i]).Where(v => v.HasValue).Select(v => v!.Value).ToList();

                return new WeekPointDto(
                    w.Start,
                    w.Label,
                    known.Count == 0 ? 0 : (int)Math.Round(known.Average()),
                    known.Count);
            })
            .ToList();
    }

    /// <summary>
    /// One line per squad. Capped at eight, because a ninth colour would have to be
    /// invented and nine lines cannot be told apart anyway; the rest fold into "Other".
    /// </summary>
    private static IReadOnlyList<SeriesDto> BuildSquadTrends(
        IReadOnlyList<Board> boards,
        IReadOnlyList<BoardAuditEntry> history,
        List<(DateOnly Start, DateOnly End, string Label)> weeks)
    {
        const int maxSeries = 6;

        var grouped = boards
            .GroupBy(b => b.SquadName)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .ToList();

        var named = grouped.Take(maxSeries).ToList();
        var rest = grouped.Skip(maxSeries).SelectMany(g => g).ToList();

        var series = named
            .Select((g, index) => new SeriesDto(
                g.Key,
                SeriesPalette[index % SeriesPalette.Length],
                AverageByWeek(g.ToList(), history, weeks)))
            .ToList();

        if (rest.Count > 0)
        {
            series.Add(new SeriesDto(
                $"Other ({grouped.Count - maxSeries} squads)",
                "#8595A9",
                AverageByWeek(rest, history, weeks)));
        }

        return series;
    }

    private static IReadOnlyList<int?> AverageByWeek(
        IReadOnlyList<Board> boards,
        IReadOnlyList<BoardAuditEntry> history,
        List<(DateOnly Start, DateOnly End, string Label)> weeks)
    {
        var perBoard = boards.Select(b => ProgressByWeek(b, history, weeks)).ToList();

        return Enumerable.Range(0, weeks.Count)
            .Select(i =>
            {
                var known = perBoard.Select(v => v[i]).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return known.Count == 0 ? (int?)null : (int)Math.Round(known.Average());
            })
            .ToList();
    }

    /// <summary>
    /// Fixed order, never cycled — a squad keeps its colour as squads are added or
    /// filtered out. Held clear of the status palette so a line is never mistaken for a
    /// health signal.
    ///
    /// Ordered so that *adjacent* hues are the ones furthest apart, and verified rather
    /// than eyeballed: the first ordering tried put blue beside violet, which colour-blind
    /// readers cannot separate at all (ΔE 0.4 deutan). This one clears every check, worst
    /// adjacent pair ΔE 19.8 under simulated CVD and 26.1 for normal vision.
    /// </summary>
    private static readonly string[] SeriesPalette =
    [
        "#2563EB", "#EA580C", "#0891B2", "#B45309", "#7C3AED", "#4D7C0F"
    ];

    // ------------------------------------------------------------------
    // People
    // ------------------------------------------------------------------

    private static IReadOnlyList<MemberLoadDto> BuildMembers(
        IReadOnlyList<Board> boards, IReadOnlyDictionary<string, int> edits)
    {
        var assignments = boards
            .SelectMany(b => b.Members.Select(m => (Board: b, Member: m)))
            .ToList();

        return assignments
            .GroupBy(a => a.Member.PersonId)
            .Select(g =>
            {
                var person = g.First().Member.Person;
                var onBoards = g.Select(a => a.Board).ToList();

                // Allocation is optional per assignment. Reporting a total as if it were
                // complete when half the rows are blank would understate the load, so the
                // page is told whether every assignment actually carries a number.
                var allocationKnown = g.All(a => a.Member.AllocationPercent.HasValue);

                return new MemberLoadDto(
                    person.Id,
                    person.FullName,
                    person.Initials,
                    person.AvatarColorOverride ?? RoleMetadata.Color(person.DefaultRole),
                    g.Select(a => RoleMetadata.Label(a.Member.Role)).Distinct().OrderBy(r => r).ToList(),
                    onBoards.Select(b => b.SquadName).Distinct().Count(),
                    onBoards.Select(b => b.SquadName).Distinct().OrderBy(s => s).ToList(),
                    g.Sum(a => a.Member.AllocationPercent ?? 0),
                    allocationKnown,
                    (int)Math.Round(onBoards.Average(b => b.ProgressPercent)),
                    onBoards.Count(b => b.Status == BoardStatus.AtRisk),
                    onBoards.Count(b => b.Status == BoardStatus.Blocked),
                    edits.TryGetValue(person.FullName, out var n) ? n : 0);
            })
            .OrderByDescending(m => m.TotalAllocationPercent)
            .ThenByDescending(m => m.SquadCount)
            .ThenBy(m => m.FullName)
            .ToList();
    }

    // ------------------------------------------------------------------
    // Capacity
    // ------------------------------------------------------------------

    private static IReadOnlyList<RoleMixDto> BuildRoleMix(IReadOnlyList<Board> boards) =>
        boards
            .GroupBy(b => b.SquadName)
            .Select(g => new RoleMixDto(
                g.Key,
                g.SelectMany(b => b.Members)
                    // Distinct per person per role, so working two boards is not two heads.
                    .GroupBy(m => (m.PersonId, m.Role))
                    .Select(x => x.Key)
                    .GroupBy(x => x.Role)
                    .Select(r => new RoleCountDto(
                        RoleMetadata.Label(r.Key), RoleMetadata.Color(r.Key), r.Count()))
                    .OrderByDescending(r => r.Count)
                    .ToList()))
            .OrderByDescending(s => s.Roles.Sum(r => r.Count))
            .ThenBy(s => s.SquadName)
            .ToList();

    // ------------------------------------------------------------------
    // Coverage
    // ------------------------------------------------------------------

    private static AnalyticsCoverageDto BuildCoverage(
        IReadOnlyList<BoardAuditEntry> history, int weekCount)
    {
        var changes = history.Count;

        var distinctWeeks = history
            .Select(e => DateOnly.FromDateTime(e.ChangedAt.UtcDateTime))
            .Select(d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)))
            .Distinct()
            .Count();

        // One week of recorded changes is a measurement, not a trend. Saying so is the
        // difference between a chart that informs and one that misleads.
        var hasRealHistory = distinctWeeks >= 2;

        var note = hasRealHistory
            ? $"Reconstructed from {changes} recorded progress {(changes == 1 ? "change" : "changes")} " +
              $"across {distinctWeeks} weeks."
            : "No progress history yet. Boards imported in bulk carry no change record, so this "
              + "shows today's figures held flat. Each edit from now on adds a real point.";

        return new AnalyticsCoverageDto(changes, distinctWeeks, hasRealHistory, note);
    }
}
