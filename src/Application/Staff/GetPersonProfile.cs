using Application.Abstractions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Staff;

/// <summary>
/// Everything the app knows about one person: what they are committed to, when they are
/// away, what work is on them, and what they have actually changed.
///
/// The activity feed is matched on the display name the audit trail records, because that
/// is what it has stored since before there was a roster to point at. Two people sharing a
/// name would merge, and a rename would break the link — the response says how confident
/// the match is rather than presenting a partial history as complete.
/// </summary>
public sealed record PersonProfileDto(
    Guid PersonId,
    string FullName,
    string Initials,
    string Color,
    string RoleLabel,
    string? Email,
    bool IsActive,
    PersonCapacityDto Capacity,
    IReadOnlyList<AssignmentDto> Assignments,
    IReadOnlyList<AvailabilityDto> Availability,
    IReadOnlyList<WorkItemDto> WorkItems,
    WorkSummaryDto Work,
    IReadOnlyList<ActivityDto> Activity,
    string ActivityNote);

/// <summary>Where they stand this week — the one figure a lead looks for first.</summary>
public sealed record PersonCapacityDto(
    int AvailablePercent,
    int CommittedPercent,
    int FreePercent,
    bool OverCommitted,
    IReadOnlyList<string> AwayReasons);

public sealed record AvailabilityDto(
    Guid Id,
    DateOnly FromDate,
    DateOnly ToDate,
    int Kind,
    string KindLabel,
    string KindColor,
    int CapacityPercent,
    string? Note,
    bool IsCurrent);

public sealed record WorkItemDto(
    Guid Id,
    Guid BoardId,
    string BoardTitle,
    Guid? PersonId,
    string? PersonName,
    string Title,
    string? Detail,
    int Status,
    string StatusLabel,
    string StatusColor,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    DateOnly? CompletedOn,
    double? EffortHours,
    bool IsOverdue);

public sealed record WorkSummaryDto(
    int Total,
    int Open,
    int Done,
    int Blocked,
    int Overdue,
    int CompletedLast30Days);

public sealed record ActivityDto(
    DateTimeOffset At,
    string BoardTitle,
    Guid BoardId,
    string Summary);

public sealed record GetPersonProfileQuery(Guid PersonId) : IRequest<PersonProfileDto>;

public sealed class GetPersonProfileQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPersonProfileQuery, PersonProfileDto>
{
    public async Task<PersonProfileDto> Handle(
        GetPersonProfileQuery request, CancellationToken cancellationToken)
    {
        var person = await db.People
            .FirstOrDefaultAsync(p => p.Id == request.PersonId, cancellationToken)
            ?? throw new KeyNotFoundException("That person was not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);

        var assignments = await db.SquadMembers
            .Include(m => m.Board)
            .Where(m => m.PersonId == person.Id)
            .ToListAsync(cancellationToken);

        var availability = await db.PersonAvailability
            .Where(a => a.PersonId == person.Id)
            .OrderByDescending(a => a.FromDate)
            .ToListAsync(cancellationToken);

        var workItems = await db.WorkItems
            .Include(w => w.Board)
            .Where(w => w.PersonId == person.Id)
            .ToListAsync(cancellationToken);

        // Matched on the recorded display name: the audit trail predates the roster link.
        var activity = await db.BoardAuditEntries
            .Where(e => e.ChangedBy == person.FullName)
            .OrderByDescending(e => e.ChangedAt)
            .Take(50)
            .Join(db.Boards, e => e.BoardId, b => b.Id,
                (e, b) => new ActivityDto(e.ChangedAt, b.Title, b.Id, e.Summary))
            .ToListAsync(cancellationToken);

        var thisWeekAway = availability.Where(a => a.Overlaps(weekStart, weekEnd)).ToList();
        var available = thisWeekAway.Count == 0 ? 100 : thisWeekAway.Min(a => a.CapacityPercent);
        var committed = assignments
            .Where(a => a.OverlapsWindow(weekStart, weekEnd))
            .Sum(a => a.AllocationPercent ?? 0);

        var cutoff = today.AddDays(-30);

        return new PersonProfileDto(
            person.Id,
            person.FullName,
            person.Initials,
            person.AvatarColorOverride ?? RoleMetadata.Color(person.DefaultRole),
            RoleMetadata.Label(person.DefaultRole),
            person.Email,
            person.IsActive,
            new PersonCapacityDto(
                available, committed, available - committed, committed > available,
                thisWeekAway.Select(a => StaffMetadata.Label(a.Kind)).Distinct().ToList()),
            assignments
                .OrderBy(a => a.Board.Title)
                .Select(a => new AssignmentDto(
                    a.BoardId, a.Board.Title, a.Board.SquadName,
                    RoleMetadata.Label(a.Role), RoleMetadata.Color(a.Role),
                    a.AllocationPercent, a.StartsOn, a.EndsOn))
                .ToList(),
            availability
                .Select(a => new AvailabilityDto(
                    a.Id, a.FromDate, a.ToDate, (int)a.Kind, StaffMetadata.Label(a.Kind),
                    StaffMetadata.Color(a.Kind), a.CapacityPercent, a.Note,
                    a.Covers(today)))
                .ToList(),
            workItems
                .OrderBy(w => w.Status == WorkItemStatus.Done)
                .ThenBy(w => w.PlannedEnd ?? DateOnly.MaxValue)
                .Select(w => Map(w, today))
                .ToList(),
            new WorkSummaryDto(
                workItems.Count,
                workItems.Count(w => w.Status != WorkItemStatus.Done),
                workItems.Count(w => w.Status == WorkItemStatus.Done),
                workItems.Count(w => w.Status == WorkItemStatus.Blocked),
                workItems.Count(w => w.IsOverdue(today)),
                workItems.Count(w => w.CompletedOn is { } done && done >= cutoff)),
            activity,
            activity.Count == 0
                ? "No recorded board changes. Edits are attributed by display name, so a " +
                  "person who has not edited a board — or whose name has changed — shows none."
                : $"{activity.Count} recorded board change(s), matched on the name " +
                  $"\"{person.FullName}\".");
    }

    internal static WorkItemDto Map(Domain.Entities.WorkItem w, DateOnly today) => new(
        w.Id,
        w.BoardId,
        w.Board?.Title ?? string.Empty,
        w.PersonId,
        w.Person?.FullName,
        w.Title,
        w.Detail,
        (int)w.Status,
        StaffMetadata.Label(w.Status),
        StaffMetadata.Color(w.Status),
        w.PlannedStart,
        w.PlannedEnd,
        w.CompletedOn,
        w.EffortHours,
        w.IsOverdue(today));
}
