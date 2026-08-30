using Domain.Common;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// A project/initiative snapshot — one slide. Owns its squad membership.
/// </summary>
public class Board : Entity
{
    private readonly List<SquadMember> _members = [];

    private Board() { }

    public Board(string title, string product, string squadName, string? sprint,
        BoardStatus status, int progressPercent, string createdBy, int orderIndex = 0)
    {
        SetTitle(title);
        SetProduct(product);
        SetSquadName(squadName);
        Sprint = Trim(sprint);
        Status = status;
        SetProgress(progressPercent);
        CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim();
        OrderIndex = orderIndex;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Product tag shown in the slide eyebrow, e.g. "VIDA HIS".</summary>
    public string Product { get; private set; } = string.Empty;

    public string SquadName { get; private set; } = string.Empty;
    public string? Sprint { get; private set; }
    public BoardStatus Status { get; private set; }
    public int ProgressPercent { get; private set; }

    public string? BlockerNote { get; private set; }
    public double? Velocity { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public string? JiraProjectKey { get; private set; }
    public string? JiraBoardId { get; private set; }

    public string CreatedBy { get; private set; } = "system";

    /// <summary>
    /// The user who owns this board. A Product Owner may write their own boards and only
    /// read everyone else's (spec section 8), so ownership is an id rather than the
    /// display name in <see cref="CreatedBy"/>, which is for audit readability.
    /// Null for seeded and imported boards, which only an Admin can edit.
    /// </summary>
    public Guid? OwnerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int OrderIndex { get; private set; }

    /// <summary>Boards are soft-deleted so audit history and exports stay resolvable.</summary>
    public bool IsDeleted { get; private set; }

    public IReadOnlyCollection<SquadMember> Members => _members;

    /// <summary>Per-role headcount, legend text and bar segments — derived, never stored.</summary>
    public SquadComposition Composition => SquadComposition.From(_members.Select(m => m.Role));

    /// <summary>
    /// Soft advisory checks. These surface as warnings on the board and never block a save
    /// (spec section 5: warning, not a hard block).
    /// </summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var warnings = new List<string>();
            var composition = Composition;

            if (composition.CountOf(Role.ProductOwner) == 0)
            {
                warnings.Add("This squad has no Product Owner.");
            }

            if (composition.CountOf(Role.Developer) == 0)
            {
                warnings.Add("This squad has no Developers.");
            }

            if (Status == BoardStatus.Blocked && string.IsNullOrWhiteSpace(BlockerNote))
            {
                warnings.Add("Status is Blocked but no blocker note has been recorded.");
            }

            return warnings;
        }
    }

    public void UpdateMeta(string title, string product, string squadName, string? sprint,
        BoardStatus status, int progressPercent, string? blockerNote, double? velocity,
        DateOnly? targetDate, string? jiraProjectKey, string? jiraBoardId)
    {
        SetTitle(title);
        SetProduct(product);
        SetSquadName(squadName);
        Sprint = Trim(sprint);
        Status = status;
        SetProgress(progressPercent);
        BlockerNote = Trim(blockerNote);
        SetVelocity(velocity);
        TargetDate = targetDate;
        JiraProjectKey = Trim(jiraProjectKey);
        JiraBoardId = Trim(jiraBoardId);
        Touch();
    }

    public SquadMember AddMember(Person person, Role role, string? detail = null,
        int? allocationPercent = null)
    {
        if (!person.IsActive)
        {
            throw new DomainException($"{person.FullName} is not an active roster member.");
        }

        if (_members.Any(m => m.PersonId == person.Id))
        {
            throw new DomainException($"{person.FullName} is already on this squad.");
        }

        var nextOrder = _members.Count == 0 ? 0 : _members.Max(m => m.OrderIndex) + 1;
        var member = new SquadMember(Id, person, role, detail, allocationPercent, nextOrder);
        _members.Add(member);
        Touch();
        return member;
    }

    public void RemoveMember(Guid memberId)
    {
        var member = _members.FirstOrDefault(m => m.Id == memberId)
                     ?? throw new DomainException("Squad member not found on this board.");

        _members.Remove(member);
        Resequence();
        Touch();
    }

    /// <summary>Applies an explicit member ordering; ids not listed keep their relative order at the end.</summary>
    public void ReorderMembers(IReadOnlyList<Guid> orderedMemberIds)
    {
        var index = 0;
        foreach (var id in orderedMemberIds)
        {
            var member = _members.FirstOrDefault(m => m.Id == id);
            member?.SetOrder(index++);
        }

        foreach (var member in _members.Where(m => !orderedMemberIds.Contains(m.Id))
                     .OrderBy(m => m.OrderIndex))
        {
            member.SetOrder(index++);
        }

        Touch();
    }

    public void SetOrder(int orderIndex)
    {
        OrderIndex = orderIndex;
        Touch();
    }

    public void AssignOwner(Guid? ownerId)
    {
        OwnerId = ownerId;
        Touch();
    }

    /// <summary>
    /// Whether the given user may write to this board. Admins may write anything;
    /// a Product Owner only their own boards; a Viewer nothing.
    /// </summary>
    public bool CanBeEditedBy(Guid userId, UserRole role) => role switch
    {
        UserRole.Admin => true,
        UserRole.ProductOwner => OwnerId == userId,
        _ => false
    };

    public void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        Touch();
    }

    public void Restore()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        Touch();
    }

    /// <summary>Deep-copies the board and its membership for the duplicate use case.</summary>
    public Board Duplicate(string createdBy, string? newTitle = null)
    {
        var copy = new Board(
            newTitle ?? $"{Title} (copy)",
            Product,
            SquadName,
            Sprint,
            Status,
            ProgressPercent,
            createdBy,
            OrderIndex + 1)
        {
            BlockerNote = BlockerNote,
            Velocity = Velocity,
            TargetDate = TargetDate,
            JiraProjectKey = JiraProjectKey,
            JiraBoardId = JiraBoardId
        };

        foreach (var member in _members.OrderBy(m => m.OrderIndex))
        {
            copy._members.Add(new SquadMember(copy.Id, member.Person, member.Role,
                member.Detail, member.AllocationPercent, member.OrderIndex));
        }

        return copy;
    }

    /// <summary>Used by the seeder and JSON import to rebuild a board with known ordering.</summary>
    public void AddMemberForImport(SquadMember member) => _members.Add(member);

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private void Resequence()
    {
        var index = 0;
        foreach (var member in _members.OrderBy(m => m.OrderIndex))
        {
            member.SetOrder(index++);
        }
    }

    private void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Board requires a title.");
        }

        Title = title.Trim();
    }

    private void SetProduct(string product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            throw new DomainException("Board requires a product tag.");
        }

        Product = product.Trim();
    }

    private void SetSquadName(string squadName)
    {
        if (string.IsNullOrWhiteSpace(squadName))
        {
            throw new DomainException("Board requires a squad name.");
        }

        SquadName = squadName.Trim();
    }

    private void SetProgress(int progressPercent)
    {
        if (progressPercent is < 0 or > 100)
        {
            throw new DomainException("Progress must be between 0 and 100.");
        }

        ProgressPercent = progressPercent;
    }

    private void SetVelocity(double? velocity)
    {
        if (velocity is < 0)
        {
            throw new DomainException("Velocity cannot be negative.");
        }

        Velocity = velocity;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
