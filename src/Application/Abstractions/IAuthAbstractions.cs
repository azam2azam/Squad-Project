using Domain.Entities;
using Domain.Enums;

namespace Application.Abstractions;

/// <summary>Issues and validates the tokens that carry a session.</summary>
public interface ITokenService
{
    /// <summary>Short-lived access token carrying the user's id, name and role.</summary>
    AccessToken CreateAccessToken(AppUser user);

    /// <summary>Opaque refresh token. The raw value goes to the client; only its hash is stored.</summary>
    RefreshToken CreateRefreshToken();

    /// <summary>Hashes a raw refresh token for storage and comparison.</summary>
    string HashRefreshToken(string rawToken);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record RefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);

/// <summary>Password hashing, kept behind an interface so the algorithm can be replaced.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time verification. Returns false rather than throwing on a bad hash.</summary>
    bool Verify(string password, string hash);
}

/// <summary>
/// Authorisation decisions that need more than a role claim — specifically board
/// ownership, which the endpoint cannot know without loading the board.
/// </summary>
public interface IBoardAuthorizer
{
    /// <summary>Throws <see cref="ForbiddenException"/> unless the current user may write to this board.</summary>
    Task EnsureCanEditAsync(Guid boardId, CancellationToken cancellationToken = default);

    /// <summary>Throws unless the current user may write to boards at all.</summary>
    void EnsureCanCreate();

    /// <summary>Throws unless the current user is an Admin.</summary>
    void EnsureIsAdmin();
}

/// <summary>Raised when an authenticated user lacks permission. Maps to 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Raised when credentials are wrong or a session has expired. Maps to 401.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>Extends <see cref="ICurrentUser"/> with the strongly-typed identity RBAC needs.</summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}
