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

    public ICollection<SquadMember> Assignments { get; private set; } = new List<SquadMember>();

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
