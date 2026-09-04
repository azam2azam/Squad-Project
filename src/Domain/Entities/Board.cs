using System.Globalization;
using Domain.Common;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// A project/initiative snapshot â one slide. Owns its squad membership.
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

    /// <summary>How likely this is to go wrong, tracked separately from Status.</summary>
    public RiskLevel RiskLevel { get; private set; }

    /// <summary>What the risk actually is. Required once risk is Medium or above.</summary>
    public string? RiskNote { get; private set; }
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

    /// <summary>Per-role headcount, legend text and bar segments â derived, never stored.</summary>
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

            // A risk with no description is unactionable â it tells a reviewer to worry
            // without telling them what about.
            if (RiskLevelMetadata.IsNotable(RiskLevel) && string.IsNullOrWhiteSpace(RiskNote))
            {
                warnings.Add(
                    $"Risk is {RiskLevelMetadata.Label(RiskLevel)} but no risk note explains why.");
            }

            return warnings;
        }
    }

    public void UpdateMeta(string title, string product, string squadName, string? sprint,
        BoardStatus status, int progressPercent, string? blockerNote, double? velocity,
        DateOnly? targetDate, string? jiraProjectKey, string? jiraBoardId,
        RiskLevel riskLevel = RiskLevel.None, string? riskNote = null)
    {
        RiskLevel = riskLevel;
        RiskNote = Trim(riskNote);

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
            RiskLevel = RiskLevel,
            RiskNote = RiskNote,
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

    /// <summary>
    /// Applies figures pulled from Jira.
    ///
    /// Deliberately narrow: a sync owns sprint, progress and status, and never touches the
    /// fields a Product Owner writes by hand — the blocker note, the risk, the roster.
    /// Automation that silently rewrites someone's commentary is worse than no automation.
    ///
    /// Returns the fields that actually changed, so the caller audits real edits only and an
    /// unchanged board does not generate noise every sync interval.
    /// </summary>
    public IReadOnlyList<JiraFieldChange> ApplyJiraSnapshot(
        string? sprint, int progressPercent, BoardStatus status)
    {
        var changes = new List<JiraFieldChange>();

        var newSprint = Trim(sprint);
        if (newSprint is not null && newSprint != Sprint)
        {
            changes.Add(new JiraFieldChange(nameof(Sprint), Sprint, newSprint));
            Sprint = newSprint;
        }

        if (progressPercent != ProgressPercent)
        {
            changes.Add(new JiraFieldChange(nameof(ProgressPercent),
                ProgressPercent.ToString(CultureInfo.InvariantCulture),
                progressPercent.ToString(CultureInfo.InvariantCulture)));
            SetProgress(progressPercent);
        }

        if (status != Status)
        {
            changes.Add(new JiraFieldChange(nameof(Status),
                BoardStatusMetadata.Label(Status), BoardStatusMetadata.Label(status)));
            Status = status;
        }

        if (changes.Count > 0)
        {
            Touch();
        }

        return changes;
    }
}

/// <summary>One field a Jira sync changed, in the shape the audit trail records.</summary>
public sealed record JiraFieldChange(string Field, string? OldValue, string? NewValue);
