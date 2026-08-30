using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Auth;

/// <summary>
/// Issues JWT access tokens and opaque refresh tokens.
///
/// Access tokens are short-lived and carry the role, so most authorisation needs no
/// database round trip. Refresh tokens are random opaque strings — never JWTs — and only
/// their SHA-256 hash is persisted, so a database leak yields nothing usable.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _key;
    private readonly int _accessMinutes;
    private readonly int _refreshDays;

    public TokenService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");

        _issuer = section["Issuer"] ?? "squad-status-board";
        _audience = section["Audience"] ?? "squad-status-board-web";
        _accessMinutes = section.GetValue("AccessTokenMinutes", 30);
        _refreshDays = section.GetValue("RefreshTokenDays", 14);

        var secret = section["SigningKey"] ?? section["SigningKey"];

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 characters. " +
                "Set it via environment variable or secret store — never commit it.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public AccessToken CreateAccessToken(AppUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_accessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            // Both the framework claim type and a plain "role" claim, so standard
            // [Authorize(Roles=...)] and any OIDC-shaped consumer both work.
            new(ClaimTypes.Role, user.Role.ToString()),
            new("role", user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshToken CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return new RefreshToken(raw, HashRefreshToken(raw),
            DateTimeOffset.UtcNow.AddDays(_refreshDays));
    }

    public string HashRefreshToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
