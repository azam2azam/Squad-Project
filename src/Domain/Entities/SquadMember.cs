using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A <see cref="Person"/>'s assignment to a <see cref="Board"/>. The role here may
/// deliberately differ from the person's default (a Tech Lead can be a Developer on
/// one squad), so it is stored on the assignment rather than read through.
/// </summary>
public class SquadMember : Entity
{
    private SquadMember() { }

    public SquadMember(Guid boardId, Person person, Role role, string? detail = null,
        int? allocationPercent = null, int orderIndex = 0)
    {
        BoardId = boardId;
        PersonId = person.Id;
        Person = person;
        Role = role;
        Detail = string.IsNullOrWhiteSpace(detail) ? person.DefaultDetail : detail.Trim();
        SetAllocation(allocationPercent);
        OrderIndex = orderIndex;
    }

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

    public Guid PersonId { get; private set; }
    public Person Person { get; private set; } = null!;

    public Role Role { get; private set; }

    /// <summary>Optional detail line on the avatar card; falls back to the person's default.</summary>
    public string? Detail { get; private set; }

    /// <summary>Optional 0-100 allocation to this squad.</summary>
    public int? AllocationPercent { get; private set; }

    public int OrderIndex { get; private set; }

    public void Update(Role role, string? detail, int? allocationPercent)
    {
        Role = role;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        SetAllocation(allocationPercent);
    }

    public void SetOrder(int orderIndex) => OrderIndex = orderIndex;

    private void SetAllocation(int? allocationPercent)
    {
        if (allocationPercent is < 0 or > 100)
        {
            throw new DomainException("Allocation must be between 0 and 100.");
        }

        AllocationPercent = allocationPercent;
    }
}
