using Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's PBKDF2 hasher, which handles salting, iteration counts
/// and constant-time comparison. Behind <see cref="IPasswordHasher"/> so the algorithm
/// can be swapped without touching any use-case.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    // The generic argument is only a marker; the algorithm does not depend on it.
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object Subject = new();

    public string Hash(string password) => _hasher.HashPassword(Subject, password);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(Subject, hash, password);
            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // A malformed stored hash is a failed verification, not a crash.
            return false;
        }
    }
}
