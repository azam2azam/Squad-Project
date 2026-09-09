using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A piece of work on a board, optionally assigned to somebody.
///
/// App-native rather than mirrored from Jira on purpose: the tracker this portfolio came
/// from shows that most of what delays a revamp is not a Jira issue at all — BRD sign-off,
/// UAT windows, ARB reviews, data migration. Those need somewhere to live, and everyone
/// on the roster can be assigned one whether or not they hold a Jira licence.
///
/// The assignee is nullable because unassigned work is a real and important state: it is
/// exactly what a delivery lead is looking for when they ask who has capacity.
/// </summary>
public class WorkItem : Entity
{
    private WorkItem() { }

    public WorkItem(Guid boardId, string title, Guid? personId, WorkItemStatus status,
        DateOnly? plannedStart, DateOnly? plannedEnd, double? effortHours,
        string? detail, string createdBy)
    {
        if (boardId == Guid.Empty)
        {
            throw new DomainException("A work item must belong to a board.");
        }

        BoardId = boardId;
        SetTitle(title);
        PersonId = personId;
        SetSchedule(plannedStart, plannedEnd);
        SetEffort(effortHours);
        Detail = Trim(detail);
        CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;

        // Set last so a task created as already Done gets its completion date.
        SetStatus(status);
    }

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

    /// <summary>Null is unassigned — the state a lead scans for when filling capacity.</summary>
    public Guid? PersonId { get; private set; }
    public Person? Person { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public WorkItemStatus Status { get; private set; }

    public DateOnly? PlannedStart { get; private set; }
    public DateOnly? PlannedEnd { get; private set; }

    /// <summary>
    /// Stamped when the status becomes Done and cleared if it moves back, so "what did
    /// they finish last month" is answerable without replaying the audit trail.
    /// </summary>
    public DateOnly? CompletedOn { get; private set; }

    /// <summary>Optional estimate. Absent is common and must not be read as zero.</summary>
    public double? EffortHours { get; private set; }

    public string CreatedBy { get; private set; } = "system";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDone => Status == WorkItemStatus.Done;

    /// <summary>Past its planned end and still not finished — the only derived alarm here.</summary>
    public bool IsOverdue(DateOnly today) =>
        !IsDone && PlannedEnd is { } end && end < today;

    public void Update(string title, Guid? personId, WorkItemStatus status,
        DateOnly? plannedStart, DateOnly? plannedEnd, double? effortHours, string? detail)
    {
        SetTitle(title);
        PersonId = personId;
        SetSchedule(plannedStart, plannedEnd);
        SetEffort(effortHours);
        Detail = Trim(detail);
        SetStatus(status);
        Touch();
    }

    public void Assign(Guid? personId)
    {
        PersonId = personId;
        Touch();
    }

    private void SetStatus(WorkItemStatus status)
    {
        Status = status;

        // Keep the completion date honest in both directions: re-opening a task that was
        // marked done must not leave a date claiming it finished.
        CompletedOn = status == WorkItemStatus.Done
            ? CompletedOn ?? DateOnly.FromDateTime(DateTime.UtcNow)
            : null;
    }

    private void SetTitle(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException("A work item needs a title.");
        }

        if (trimmed.Length > 300)
        {
            throw new DomainException("A work item title must be 300 characters or fewer.");
        }

        Title = trimmed;
    }

    private void SetSchedule(DateOnly? plannedStart, DateOnly? plannedEnd)
    {
        if (plannedStart is { } start && plannedEnd is { } end && end < start)
        {
            throw new DomainException("A work item cannot be planned to end before it starts.");
        }

        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
    }

    private void SetEffort(double? effortHours)
    {
        if (effortHours is { } hours && (hours < 0 || hours > 10000))
        {
            throw new DomainException("Effort must be between 0 and 10000 hours.");
        }

        EffortHours = effortHours;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
