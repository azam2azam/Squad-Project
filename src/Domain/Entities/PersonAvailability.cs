using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A period where someone's capacity differs from normal — leave, a public holiday,
/// training, or part-time working.
///
/// Modelled as an **exception**, not a calendar. Recording every working day for every
/// person would be a lot of rows saying "yes, they were here"; recording only the
/// departures from normal keeps the data small and the meaning obvious.
///
/// <see cref="CapacityPercent"/> is what remains, not what is lost: 0 is fully away,
/// 50 is half days. That reads the same way as an assignment's allocation, so the two can
/// be compared without anyone having to remember which direction each one runs.
/// </summary>
public class PersonAvailability : Entity
{
    private PersonAvailability() { }

    public PersonAvailability(Guid personId, DateOnly fromDate, DateOnly toDate,
        AvailabilityKind kind, int capacityPercent, string? note, string recordedBy)
    {
        if (personId == Guid.Empty)
        {
            throw new DomainException("An availability record needs a person.");
        }

        PersonId = personId;
        SetPeriod(fromDate, toDate);
        Kind = kind;
        SetCapacity(capacityPercent);
        Note = Trim(note);
        RecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? "system" : recordedBy.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid PersonId { get; private set; }
    public Person Person { get; private set; } = null!;

    /// <summary>Inclusive on both ends: a single day off has From == To.</summary>
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    public AvailabilityKind Kind { get; private set; }

    /// <summary>Capacity that <em>remains</em> during the period. 0 means fully away.</summary>
    public int CapacityPercent { get; private set; }

    public string? Note { get; private set; }
    public string RecordedBy { get; private set; } = "system";
    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(DateOnly fromDate, DateOnly toDate, AvailabilityKind kind,
        int capacityPercent, string? note)
    {
        SetPeriod(fromDate, toDate);
        Kind = kind;
        SetCapacity(capacityPercent);
        Note = Trim(note);
    }

    /// <summary>Whether this period covers a given day, both ends inclusive.</summary>
    public bool Covers(DateOnly day) => day >= FromDate && day <= ToDate;

    /// <summary>Whether this period touches a window at all — used for week buckets.</summary>
    public bool Overlaps(DateOnly windowStart, DateOnly windowEnd) =>
        FromDate <= windowEnd && ToDate >= windowStart;

    private void SetPeriod(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
        {
            throw new DomainException("An availability period cannot end before it starts.");
        }

        FromDate = fromDate;
        ToDate = toDate;
    }

    private void SetCapacity(int capacityPercent)
    {
        if (capacityPercent is < 0 or > 100)
        {
            throw new DomainException(
                "Remaining capacity must be between 0 and 100 percent.");
        }

        CapacityPercent = capacityPercent;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
