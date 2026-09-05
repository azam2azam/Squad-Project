using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Users;

/// <summary>
/// Managing who can sign in (spec section 8).
///
/// Every handler here is admin-only, and two rules exist to stop an administrator
/// locking the organisation out of its own board: nobody may deactivate or demote
/// themselves, and the last active administrator may not be removed by anyone. Both are
/// enforced here rather than in the UI, because the UI is not the security boundary.
///
/// A password is only ever accepted, never returned — no handler puts a hash in a DTO.
/// </summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    string RoleLabel,
    bool IsActive,
    bool HasPassword,
    Guid? PersonId,
    string? PersonName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public static class UserRoleMetadata
{
    /// <summary>Most privileged first, which is the order an admin scans.</summary>
    public static readonly UserRole[] DisplayOrder =
        [UserRole.Admin, UserRole.ProductOwner, UserRole.Viewer];

    public static string Label(UserRole role) => role switch
    {
        UserRole.Admin => "Admin",
        UserRole.ProductOwner => "Product Owner",
        UserRole.Viewer => "Viewer",
        _ => role.ToString()
    };

    public static string Description(UserRole role) => role switch
    {
        UserRole.Admin => "Everything: all boards, the roster, users, imports and Jira.",
        UserRole.ProductOwner => "Creates and edits their own boards; reads everyone else's.",
        UserRole.Viewer => "Read, present and export. Cannot change anything.",
        _ => string.Empty
    };
}

// ---------------------------------------------------------------------------
// List
// ---------------------------------------------------------------------------

public sealed record ListUsersQuery(string? Search, bool IncludeInactive, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<UserDto>>;

public sealed class ListUsersQueryHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<ListUsersQuery, PagedResult<UserDto>>
{
    public async Task<PagedResult<UserDto>> Handle(
        ListUsersQuery request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var query = db.Users.AsQueryable();

        if (!request.IncludeInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u => u.Email.Contains(term) || u.DisplayName.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 200);

        var users = await query
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.DisplayName)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // Resolve linked roster names in one round trip rather than per row.
        var personIds = users.Where(u => u.PersonId.HasValue).Select(u => u.PersonId!.Value).ToList();
        var people = personIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.People
                .Where(p => personIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);

        return new PagedResult<UserDto>(
            users.Select(u => Map(u, people)).ToList(), page, size, total);
    }

    internal static UserDto Map(AppUser user, IReadOnlyDictionary<Guid, string> people) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        UserRoleMetadata.Label(user.Role),
        user.IsActive,
        // Whether a local password exists, never the hash itself.
        !string.IsNullOrWhiteSpace(user.PasswordHash),
        user.PersonId,
        user.PersonId is { } id && people.TryGetValue(id, out var name) ? name : null,
        user.CreatedAt,
        user.LastLoginAt);
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public sealed record CreateUserCommand(
    string Email,
    string DisplayName,
    UserRole Role,
    string Password,
    Guid? PersonId) : IRequest<UserDto>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Password)
            .NotEmpty()
            .MinimumLength(PasswordRules.MinimumLength)
            .WithMessage($"Password must be at least {PasswordRules.MinimumLength} characters.");
    }
}

public static class PasswordRules
{
    /// <summary>Length is the honest lever here; complexity rules mostly produce Passw0rd!.</summary>
    public const int MinimumLength = 12;
}

public sealed class CreateUserCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, IPasswordHasher hasher)
    : IRequestHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new DomainException($"{email} already has an account.");
        }

        if (request.PersonId is { } personId
            && !await db.People.AnyAsync(p => p.Id == personId, cancellationToken))
        {
            throw new DomainException("That roster member no longer exists.");
        }

        var user = new AppUser(email, request.DisplayName, request.Role,
            hasher.Hash(request.Password));

        if (request.PersonId is { } linked)
        {
            user.LinkToPerson(linked);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return ListUsersQueryHandler.Map(user, await NamesFor(db, user, cancellationToken));
    }

    internal static async Task<IReadOnlyDictionary<Guid, string>> NamesFor(
        IAppDbContext db, AppUser user, CancellationToken cancellationToken)
    {
        if (user.PersonId is not { } id) return new Dictionary<Guid, string>();

        var name = await db.People
            .Where(p => p.Id == id)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(cancellationToken);

        return name is null
            ? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string> { [id] = name };
    }
}

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

public sealed record UpdateUserCommand(
    Guid Id,
    string DisplayName,
    UserRole Role,
    Guid? PersonId) : IRequest<UserDto>;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateUserCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, ICurrentUserContext currentUser)
    : IRequestHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That user was not found.");

        if (user.Role == UserRole.Admin && request.Role != UserRole.Admin)
        {
            // Demoting yourself would end the session that is doing the demoting.
            if (currentUser.UserId == user.Id)
            {
                throw new DomainException(
                    "You cannot change your own role. Ask another administrator.");
            }

            await GuardLastAdmin(db, user.Id, cancellationToken);
        }

        user.Rename(request.DisplayName);

        if (user.Role != request.Role)
        {
            user.ChangeRole(request.Role);

            // The access token carries the old role until it expires, so end the session:
            // without this a demoted user keeps their old rights until they sign out.
            user.ClearRefreshToken();
        }

        if (request.PersonId is { } personId)
        {
            if (!await db.People.AnyAsync(p => p.Id == personId, cancellationToken))
            {
                throw new DomainException("That roster member no longer exists.");
            }

            user.LinkToPerson(personId);
        }
        else
        {
            user.UnlinkPerson();
        }

        await db.SaveChangesAsync(cancellationToken);

        return ListUsersQueryHandler.Map(
            user, await CreateUserCommandHandler.NamesFor(db, user, cancellationToken));
    }

    /// <summary>An organisation with no administrator cannot get one back without a DBA.</summary>
    internal static async Task GuardLastAdmin(
        IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var otherAdmins = await db.Users
            .CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != userId,
                cancellationToken);

        if (otherAdmins == 0)
        {
            throw new DomainException(
                "This is the last active administrator. Promote someone else first.");
        }
    }
}

// ---------------------------------------------------------------------------
// Activate / deactivate
// ---------------------------------------------------------------------------

public sealed record SetUserActiveCommand(Guid Id, bool IsActive) : IRequest<UserDto>;

public sealed class SetUserActiveCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, ICurrentUserContext currentUser)
    : IRequestHandler<SetUserActiveCommand, UserDto>
{
    public async Task<UserDto> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That user was not found.");

        if (!request.IsActive)
        {
            if (currentUser.UserId == user.Id)
            {
                throw new DomainException("You cannot deactivate your own account.");
            }

            if (user.Role == UserRole.Admin)
            {
                await UpdateUserCommandHandler.GuardLastAdmin(db, user.Id, cancellationToken);
            }

            user.Deactivate();

            // Ends any live session rather than letting it run to expiry.
            user.ClearRefreshToken();
        }
        else
        {
            user.Reactivate();
        }

        await db.SaveChangesAsync(cancellationToken);

        return ListUsersQueryHandler.Map(
            user, await CreateUserCommandHandler.NamesFor(db, user, cancellationToken));
    }
}

// ---------------------------------------------------------------------------
// Reset another user's password
// ---------------------------------------------------------------------------

public sealed record ResetUserPasswordCommand(Guid Id, string NewPassword) : IRequest;

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(c => c.NewPassword)
            .NotEmpty()
            .MinimumLength(PasswordRules.MinimumLength)
            .WithMessage($"Password must be at least {PasswordRules.MinimumLength} characters.");
    }
}

public sealed class ResetUserPasswordCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, IPasswordHasher hasher)
    : IRequestHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That user was not found.");

        user.SetPasswordHash(hasher.Hash(request.NewPassword));

        // Whoever held the old session should not keep it after a password reset.
        user.ClearRefreshToken();

        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Change your own password
// ---------------------------------------------------------------------------

/// <summary>
/// Available to any signed-in user, and the reason an admin-set password is safe: the
/// person can replace the one the admin knows. Requires the current password, so a
/// borrowed session cannot lock the owner out.
/// </summary>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : IRequest;

public sealed class ChangeOwnPasswordCommandValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty();
        RuleFor(c => c.NewPassword)
            .NotEmpty()
            .MinimumLength(PasswordRules.MinimumLength)
            .WithMessage($"Password must be at least {PasswordRules.MinimumLength} characters.");
        RuleFor(c => c.NewPassword)
            .NotEqual(c => c.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

public sealed class ChangeOwnPasswordCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, IPasswordHasher hasher)
    : IRequestHandler<ChangeOwnPasswordCommand>
{
    public async Task Handle(ChangeOwnPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedAccessException("Sign in to change your password.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new KeyNotFoundException("That user was not found.");

        if (user.PasswordHash is null || !hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new DomainException("The current password is not correct.");
        }

        user.SetPasswordHash(hasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(cancellationToken);
    }
}
