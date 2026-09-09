using System.Globalization;
using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Staff;

/// <summary>
/// The team schedule: who is committed where, week by week, and what is left over.
///
/// The capacity model is deliberately simple enough to explain in a sentence, because a
/// number nobody can explain is a number nobody trusts:
///
///   available = 100 − whatever availability records take away
///   committed = the sum of allocations on assignments live that week
///   free      = available − committed   (negative means over-committed)
///
/// An assignment with no allocation recorded contributes **nothing** rather than a guessed
/// share. That understates commitment, and the response says how many assignments were
/// silent so the reader knows which way the number is wrong.
/// </summary>
public sealed record StaffScheduleDto(
    IReadOnlyList<WeekColumnDto> Weeks,
    IReadOnlyList<StaffRowDto> People,
    ScheduleCoverageDto Coverage);

public sealed record WeekColumnDto(DateOnly Start, DateOnly End, string Label, bool IsCurrent);

public sealed record StaffRowDto(
    Guid PersonId,
    string FullName,
    string Initials,
    string Color,
    string RoleLabel,
    bool IsActive,
    IReadOnlyList<WeekCellDto> Weeks,
    IReadOnlyList<AssignmentDto> Assignments,
    int OpenWorkItems,
    int OverdueWorkItems);

/// <summary>One person, one week. Percentages, so 100 is a full week.</summary>
public sealed record WeekCellDto(
    int AvailablePercent,
    int CommittedPercent,
    int FreePercent,
    bool OverCommitted,
    IReadOnlyList<string> Boards,
    IReadOnlyList<string> AwayReasons);

public sealed record AssignmentDto(
    Guid BoardId,
    string BoardTitle,
    string SquadName,
    string RoleLabel,
    string RoleColor,
    int? AllocationPercent,
    DateOnly? StartsOn,
    DateOnly? EndsOn);

/// <summary>How complete the underlying data is, so the page can qualify what it shows.</summary>
public sealed record ScheduleCoverageDto(
    int Assignments,
    int AssignmentsWithoutAllocation,
    int AssignmentsWithoutDates,
    string Note);

public sealed record GetStaffScheduleQuery(DateOnly? From = null, int Weeks = 8)
    : IRequest<StaffScheduleDto>;

public sealed class GetStaffScheduleQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStaffScheduleQuery, StaffScheduleDto>
{
    public async Task<StaffScheduleDto> Handle(
        GetStaffScheduleQuery request, CancellationToken cancellationToken)
    {
        var weeks = BuildWeeks(request.From, Math.Clamp(request.Weeks, 2, 26));
        var windowStart = weeks[0].Start;
        var windowEnd = weeks[^1].End;

        var people = await db.People
            .Where(p => p.IsActive)
            .OrderBy(p => p.FullName)
            .ToListAsync(cancellationToken);

        var assignments = await db.SquadMembers
            .Include(m => m.Board)
            .ToListAsync(cancellationToken);

        var availability = await db.PersonAvailability
            .Where(a => a.FromDate <= windowEnd && a.ToDate >= windowStart)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var workItems = await db.WorkItems
            .Where(w => w.PersonId != null)
            .Select(w => new { w.PersonId, w.Status, w.PlannedEnd })
            .ToListAsync(cancellationToken);

        var rows = people
            .Select(person => BuildRow(person, assignments, availability, workItems
                .Where(w => w.PersonId == person.Id)
                .Select(w => (w.Status, w.PlannedEnd))
                .ToList(), weeks, today))
            .ToList();

        return new StaffScheduleDto(
            weeks.Select(w => new WeekColumnDto(w.Start, w.End, w.Label,
                today >= w.Start && today <= w.End)).ToList(),
            rows,
            BuildCoverage(assignments));
    }

    private static StaffRowDto BuildRow(
        Person person,
        IReadOnlyList<SquadMember> allAssignments,
        IReadOnlyList<PersonAvailability> allAvailability,
        IReadOnlyList<(WorkItemStatus Status, DateOnly? PlannedEnd)> work,
        IReadOnlyList<(DateOnly Start, DateOnly End, string Label)> weeks,
        DateOnly today)
    {
        var mine = allAssignments.Where(a => a.PersonId == person.Id).ToList();
        var away = allAvailability.Where(a => a.PersonId == person.Id).ToList();

        var cells = weeks.Select(week =>
        {
            // The most restrictive overlapping record wins: somebody on leave for two days
            // of a week they are also part-time is limited by the leave.
            var overlapping = away.Where(a => a.Overlaps(week.Start, week.End)).ToList();
            var available = overlapping.Count == 0
                ? 100
                : overlapping.Min(a => a.CapacityPercent);

            var live = mine.Where(a => a.OverlapsWindow(week.Start, week.End)).ToList();
            var committed = live.Sum(a => a.AllocationPercent ?? 0);

            return new WeekCellDto(
                available,
                committed,
                available - committed,
                committed > available,
                live.Select(a => a.Board.Title).Distinct().ToList(),
                overlapping.Select(a => StaffMetadata.Label(a.Kind)).Distinct().ToList());
        }).ToList();

        return new StaffRowDto(
            person.Id,
            person.FullName,
            person.Initials,
            person.AvatarColorOverride ?? RoleMetadata.Color(person.DefaultRole),
            RoleMetadata.Label(person.DefaultRole),
            person.IsActive,
            cells,
            mine.OrderBy(a => a.Board.Title)
                .Select(a => new AssignmentDto(
                    a.BoardId, a.Board.Title, a.Board.SquadName,
                    RoleMetadata.Label(a.Role), RoleMetadata.Color(a.Role),
                    a.AllocationPercent, a.StartsOn, a.EndsOn))
                .ToList(),
            work.Count(w => w.Status != WorkItemStatus.Done),
            work.Count(w => w.Status != WorkItemStatus.Done
                            && w.PlannedEnd is { } end && end < today));
    }

    /// <summary>Week buckets starting on a Monday, from the requested date or this week.</summary>
    private static List<(DateOnly Start, DateOnly End, string Label)> BuildWeeks(
        DateOnly? from, int count)
    {
        var anchor = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));

        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var start = monday.AddDays(7 * i);
                return (start, start.AddDays(6),
                    start.ToString("d MMM", CultureInfo.InvariantCulture));
            })
            .ToList();
    }

    private static ScheduleCoverageDto BuildCoverage(IReadOnlyList<SquadMember> assignments)
    {
        var withoutAllocation = assignments.Count(a => a.AllocationPercent is null);
        var withoutDates = assignments.Count(a => a.StartsOn is null && a.EndsOn is null);

        var parts = new List<string>();

        if (withoutAllocation > 0)
        {
            parts.Add($"{withoutAllocation} of {assignments.Count} assignments have no " +
                      "allocation recorded and count as zero, so commitment is understated");
        }

        if (withoutDates > 0)
        {
            parts.Add($"{withoutDates} have no dates and are treated as running throughout");
        }

        return new ScheduleCoverageDto(
            assignments.Count,
            withoutAllocation,
            withoutDates,
            parts.Count == 0
                ? "Every assignment has an allocation and dates."
                : string.Join("; ", parts) + ".");
    }
}
