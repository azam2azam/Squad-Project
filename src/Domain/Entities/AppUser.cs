using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A person who can sign in. Deliberately separate from <see cref="Person"/>: the roster
/// is who appears on slides, this is who has access. Most roster members never log in,
/// and an admin need not appear on any squad.
///
/// The password fields exist for the local login stub. When the deployment federates with
/// corporate OIDC, <see cref="ExternalSubject"/> carries the provider's subject claim and
/// the hash goes unused (spec section 8).
/// </summary>
public class AppUser : Entity
{
    private AppUser() { }

    public AppUser(string email, string displayName, UserRole role,
        string? passwordHash = null, string? externalSubject = null)
    {
        SetEmail(email);
        SetDisplayName(displayName);
        Role = role;
        PasswordHash = passwordHash;
        ExternalSubject = externalSubject;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }

    /// <summary>Null for users that only ever authenticate through an external provider.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>The OIDC 'sub' claim once federated. Null for local accounts.</summary>
    public string? ExternalSubject { get; private set; }

    /// <summary>Optional link to the roster, so a signed-in PO can be shown on a slide.</summary>
    public Guid? PersonId { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Hashed refresh token; null once the session is revoked or expired.</summary>
    public string? RefreshTokenHash { get; private set; }

    public DateTimeOffset? RefreshTokenExpiresAt { get; private set; }

    public void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new DomainException("Password hash cannot be empty.");
        }

        PasswordHash = hash;
    }

    public void LinkToPerson(Guid personId) => PersonId = personId;

    public void ChangeRole(UserRole role) => Role = role;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Stores a refresh token. Only the hash is kept, so a database leak does not hand
    /// out usable sessions.
    /// </summary>
    public void SetRefreshToken(string hash, DateTimeOffset expiresAt)
    {
        RefreshTokenHash = hash;
        RefreshTokenExpiresAt = expiresAt;
    }

    public void ClearRefreshToken()
    {
        RefreshTokenHash = null;
        RefreshTokenExpiresAt = null;
    }

    public bool RefreshTokenIsValid(string hash) =>
        RefreshTokenHash is not null
        && RefreshTokenHash == hash
        && RefreshTokenExpiresAt > DateTimeOffset.UtcNow;

    private void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainException("A user requires a valid email address.");
        }

        Email = email.Trim().ToLowerInvariant();
    }

    private void SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("A user requires a display name.");
        }

        DisplayName = displayName.Trim();
    }
}
