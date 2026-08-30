namespace Application.Abstractions;

/// <summary>Ambient identity for authorisation checks and audit stamping.</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Display name used in the audit log; falls back to "system" for background work.</summary>
    string DisplayName { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
