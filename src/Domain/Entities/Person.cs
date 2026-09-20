using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A member of the org-wide reusable roster. People are picked, not retyped.
/// Deletion is always soft so historical <see cref="SquadMember"/> rows stay intact.
/// </summary>
public class Person : Entity
{
    private Person() { }

    public Person(string fullName, Role defaultRole, string? defaultDetail = null,
        string? email = null, string? avatarColorOverride = null)
    {
        SetFullName(fullName);
        DefaultRole = defaultRole;
        DefaultDetail = Trim(defaultDetail);
        Email = Trim(email);
        AvatarColorOverride = Trim(avatarColorOverride);
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string FullName { get; private set; } = string.Empty;
    public Role DefaultRole { get; private set; }

    /// <summary>Free-text skill line, e.g. "Angular · FHIR R4".</summary>
    public string? DefaultDetail { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Overrides the role colour for this person's avatar when set.</summary>
    public string? AvatarColorOverride { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // -----------------------------------------------------------------------
    // Profile — what a person says about themselves
    // -----------------------------------------------------------------------

    /// <summary>
    /// One line under the name: "Tech Lead · Java Full Stack". Separate from
    /// <see cref="DefaultDetail"/>, which is the stack line printed on a slide — this one is
    /// written by the person, that one is set by whoever maintains the roster.
    /// </summary>
    public string? Headline { get; private set; }

    /// <summary>A paragraph or two. Deliberately not long: nobody reads a CV on a board.</summary>
    public string? About { get; private set; }

    public ICollection<SquadMember> Assignments { get; private set; } = new List<SquadMember>();

    private readonly List<PersonSkill> _skills = [];
    public IReadOnlyCollection<PersonSkill> Skills => _skills;

    private readonly List<PersonAchievement> _achievements = [];
    public IReadOnlyCollection<PersonAchievement> Achievements => _achievements;

    /// <summary>
    /// The photo, held in its own row so a list of forty people does not drag forty images
    /// out of the database with it.
    /// </summary>
    public PersonPhoto? Photo { get; private set; }

    /// <summary>
    /// Edits the parts a person writes about themselves. Kept apart from
    /// <see cref="Update"/> because the two have different owners: this is the person, that
    /// is the roster administrator.
    /// </summary>
    public void UpdateProfile(string? headline, string? about)
    {
        Headline = Trim(headline);
        About = Trim(about);
        Touch();
    }

    /// <summary>
    /// Adds a skill, ignoring one that is already there whatever its casing — "Angular" and
    /// "angular" are the same skill, and a directory that lists both is a worse directory.
    /// </summary>
    public PersonSkill AddSkill(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainException("A skill needs a name.");
        }

        if (trimmed.Length > 60)
        {
            throw new DomainException("A skill name cannot be longer than 60 characters.");
        }

        var existing = _skills.FirstOrDefault(
            s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) return existing;

        var skill = new PersonSkill(Id, trimmed, _skills.Count);
        _skills.Add(skill);
        Touch();

        return skill;
    }

    public void RemoveSkill(Guid skillId)
    {
        var skill = _skills.FirstOrDefault(s => s.Id == skillId);
        if (skill is null) return;

        _skills.Remove(skill);
        Resequence(_skills);
        Touch();
    }

    public PersonAchievement AddAchievement(string title, string? detail, DateOnly? achievedOn)
    {
        var achievement = new PersonAchievement(Id, title, detail, achievedOn, _achievements.Count);
        _achievements.Add(achievement);
        Touch();

        return achievement;
    }

    public void RemoveAchievement(Guid achievementId)
    {
        var achievement = _achievements.FirstOrDefault(a => a.Id == achievementId);
        if (achievement is null) return;

        _achievements.Remove(achievement);
        Resequence(_achievements);
        Touch();
    }

    /// <summary>Replaces the photo, or clears it when given nothing.</summary>
    public void SetPhoto(byte[]? bytes, string? contentType)
    {
        if (bytes is null || bytes.Length == 0)
        {
            Photo = null;
            Touch();
            return;
        }

        Photo = new PersonPhoto(Id, bytes, contentType ?? "image/jpeg");
        Touch();
    }

    private static void Resequence<T>(List<T> items) where T : IOrdered
    {
        for (var index = 0; index < items.Count; index++)
        {
            items[index].SetOrder(index);
        }
    }

    /// <summary>Up to two initials, used by the avatar card ("Sara Al-Otaibi" -> "SA").</summary>
    public string Initials => ComputeInitials(FullName);

    public void Update(string fullName, Role defaultRole, string? defaultDetail,
        string? email, string? avatarColorOverride)
    {
        SetFullName(fullName);
        DefaultRole = defaultRole;
        DefaultDetail = Trim(defaultDetail);
        Email = Trim(email);
        AvatarColorOverride = Trim(avatarColorOverride);
        Touch();
    }

    /// <summary>Soft delete. Existing squad assignments are deliberately left in place.</summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Touch();
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
        Touch();
    }

    private void SetFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Person requires a full name.");
        }

        FullName = fullName.Trim();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string ComputeInitials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
        };
    }
}
