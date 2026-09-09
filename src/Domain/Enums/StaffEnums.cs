namespace Domain.Enums;

/// <summary>
/// Why someone's capacity differs from normal. Values are stable and persisted; do not
/// renumber.
/// </summary>
public enum AvailabilityKind
{
    Leave = 0,
    PublicHoliday = 1,
    Training = 2,
    /// <summary>A standing reduction — someone contracted for three days a week.</summary>
    PartTime = 3,
    Other = 4
}

/// <summary>
/// Where a work item is. Deliberately shorter than <see cref="BoardStatus"/>: a task is
/// either waiting, being worked, stuck, or finished, and a fifth option would only invite
/// people to disagree about what it means.
/// </summary>
public enum WorkItemStatus
{
    ToDo = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3
}

public static class StaffMetadata
{
    public static readonly AvailabilityKind[] AvailabilityOrder =
        [AvailabilityKind.Leave, AvailabilityKind.PublicHoliday, AvailabilityKind.Training,
         AvailabilityKind.PartTime, AvailabilityKind.Other];

    public static readonly WorkItemStatus[] WorkItemOrder =
        [WorkItemStatus.ToDo, WorkItemStatus.InProgress, WorkItemStatus.Blocked,
         WorkItemStatus.Done];

    public static string Label(AvailabilityKind kind) => kind switch
    {
        AvailabilityKind.Leave => "Leave",
        AvailabilityKind.PublicHoliday => "Public holiday",
        AvailabilityKind.Training => "Training",
        AvailabilityKind.PartTime => "Part time",
        _ => "Other"
    };

    public static string Label(WorkItemStatus status) => status switch
    {
        WorkItemStatus.ToDo => "To do",
        WorkItemStatus.InProgress => "In progress",
        WorkItemStatus.Blocked => "Blocked",
        _ => "Done"
    };

    /// <summary>
    /// Reuses the board status palette so a blocked task and a blocked board read as the
    /// same signal rather than two vocabularies for one idea.
    /// </summary>
    public static string Color(WorkItemStatus status) => status switch
    {
        WorkItemStatus.ToDo => "#8595A9",
        WorkItemStatus.InProgress => "#60A5FA",
        WorkItemStatus.Blocked => "#F87171",
        _ => "#34D399"
    };

    public static string Color(AvailabilityKind kind) => kind switch
    {
        AvailabilityKind.Leave => "#F59E0B",
        AvailabilityKind.PublicHoliday => "#A78BFA",
        AvailabilityKind.Training => "#38BDF8",
        AvailabilityKind.PartTime => "#94A3B8",
        _ => "#8595A9"
    };
}
