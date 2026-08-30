using Application.Abstractions;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth.Commands;

/// <summary>What the client gets back from a successful sign-in or refresh.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    SignedInUser User);

public sealed record SignedInUser(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    string RoleName);

// ---------------------------------------------------------------------------
// Login
// ---------------------------------------------------------------------------

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResult>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(320);
        RuleFor(c => c.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IAppDbContext db, IPasswordHasher hasher, ITokenService tokens)
    : IRequestHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // One message for "no such user", "wrong password" and "deactivated": telling
        // them apart hands an attacker a way to enumerate valid accounts.
        if (user is null
            || !user.IsActive
            || user.PasswordHash is null
            || !hasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Email or password is incorrect.");
        }

        user.RecordLogin();

        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();
        user.SetRefreshToken(refresh.Hash, refresh.ExpiresAt);

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            access.Value, access.ExpiresAt,
            refresh.Value, refresh.ExpiresAt,
            new SignedInUser(user.Id, user.Email, user.DisplayName, user.Role, user.Role.ToString()));
    }
}

// ---------------------------------------------------------------------------
// Refresh
// ---------------------------------------------------------------------------

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResult>;

public sealed class RefreshTokenCommandHandler(
    IAppDbContext db, ITokenService tokens)
    : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthorizedException("That session is no longer valid. Please sign in again.");
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.RefreshTokenHash == hash, cancellationToken);

        if (user is null || !user.IsActive || !user.RefreshTokenIsValid(hash))
        {
            throw new UnauthorizedException("That session is no longer valid. Please sign in again.");
        }

        var access = tokens.CreateAccessToken(user);

        // Rotated on every use: a refresh token is single-use, so a stolen one is only
        // good until the legitimate client next refreshes.
        var refresh = tokens.CreateRefreshToken();
        user.SetRefreshToken(refresh.Hash, refresh.ExpiresAt);

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            access.Value, access.ExpiresAt,
            refresh.Value, refresh.ExpiresAt,
            new SignedInUser(user.Id, user.Email, user.DisplayName, user.Role, user.Role.ToString()));
    }
}

// ---------------------------------------------------------------------------
// Logout
// ---------------------------------------------------------------------------

public sealed record LogoutCommand(Guid UserId) : IRequest;

public sealed class LogoutCommandHandler(IAppDbContext db) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return;

        user.ClearRefreshToken();
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Me
// ---------------------------------------------------------------------------

public sealed record GetCurrentUserQuery : IRequest<SignedInUser?>;

public sealed class GetCurrentUserQueryHandler(IAppDbContext db, ICurrentUserContext context)
    : IRequestHandler<GetCurrentUserQuery, SignedInUser?>
{
    public async Task<SignedInUser?> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (context.UserId is not { } id) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null
            ? null
            : new SignedInUser(user.Id, user.Email, user.DisplayName, user.Role, user.Role.ToString());
    }
}
