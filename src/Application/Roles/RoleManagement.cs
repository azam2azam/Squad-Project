using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Roles;

/// <summary>
/// The roles a squad member can hold.
///
/// Reads are open to anyone signed in — every picker in the app needs them. Writes are
/// admin-only: a role is org-wide reference data that changes what every board renders.
///
/// After any write the in-process catalogue is refreshed, so labels and colours on slides
/// and exports match the database without a restart.
/// </summary>
public sealed record SquadRoleDto(
    int Value,
    string Name,
    string Label,
    string PluralLabel,
    string Color,
    int OrderIndex,
    bool IsBuiltIn,
    bool IsActive,
    int PeopleUsing);

// ---------------------------------------------------------------------------
// Read
// ---------------------------------------------------------------------------

public sealed record ListRolesQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<SquadRoleDto>>;

public sealed class ListRolesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListRolesQuery, IReadOnlyList<SquadRoleDto>>
{
    public async Task<IReadOnlyList<SquadRoleDto>> Handle(
        ListRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await db.SquadRoles
            .Where(r => request.IncludeInactive || r.IsActive)
            .OrderBy(r => r.OrderIndex)
            .ThenBy(r => r.Value)
            .ToListAsync(cancellationToken);

        // How many people hold each role, so an admin can see what retiring one affects.
        var usage = await db.People
            .GroupBy(p => p.DefaultRole)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = usage.ToDictionary(u => (int)u.Role, u => u.Count);

        return roles
            .Select(r => new SquadRoleDto(
                r.Value, r.Name, r.Label, r.PluralLabel, r.Color, r.OrderIndex,
                r.IsBuiltIn, r.IsActive,
                counts.TryGetValue(r.Value, out var n) ? n : 0))
            .ToList();
    }
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public sealed record CreateRoleCommand(
    string Name, string Label, string? PluralLabel, string Color) : IRequest<SquadRoleDto>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(60);
        RuleFor(c => c.Label).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Color).NotEmpty();
    }
}

public sealed class CreateRoleCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, IRoleCatalog catalog)
    : IRequestHandler<CreateRoleCommand, SquadRoleDto>
{
    public async Task<SquadRoleDto> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var name = request.Name.Trim();

        if (await db.SquadRoles.AnyAsync(r => r.Name == name, cancellationToken))
        {
            throw new DomainException($"A role called {name} already exists.");
        }

        if (await db.SquadRoles.AnyAsync(r => r.Label == request.Label.Trim(), cancellationToken))
        {
            throw new DomainException($"A role displayed as \"{request.Label.Trim()}\" already exists.");
        }

        // Values are never reused, so a retired role's number cannot come back attached to
        // different people. Take one above the highest ever issued.
        var highest = await db.SquadRoles
            .Select(r => (int?)r.Value)
            .MaxAsync(cancellationToken) ?? 0;

        var value = Math.Max(highest + 1, RoleMetadata.FirstCustomValue);

        var role = new SquadRole(
            value, name, request.Label, request.PluralLabel ?? request.Label, request.Color,
            orderIndex: value);

        db.SquadRoles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        await catalog.RefreshAsync(cancellationToken);

        return new SquadRoleDto(role.Value, role.Name, role.Label, role.PluralLabel,
            role.Color, role.OrderIndex, role.IsBuiltIn, role.IsActive, 0);
    }
}

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

public sealed record UpdateRoleCommand(
    int Value, string Label, string? PluralLabel, string Color, int OrderIndex)
    : IRequest<SquadRoleDto>;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(c => c.Label).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Color).NotEmpty();
    }
}

public sealed class UpdateRoleCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, IRoleCatalog catalog)
    : IRequestHandler<UpdateRoleCommand, SquadRoleDto>
{
    public async Task<SquadRoleDto> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var role = await db.SquadRoles.FirstOrDefaultAsync(r => r.Value == request.Value, cancellationToken)
                   ?? throw new KeyNotFoundException("That role was not found.");

        var label = request.Label.Trim();

        if (await db.SquadRoles.AnyAsync(
                r => r.Label == label && r.Value != request.Value, cancellationToken))
        {
            throw new DomainException($"A role displayed as \"{label}\" already exists.");
        }

        // A built-in may be renamed and recoloured — an org that calls it "Delivery Lead"
        // should be able to say so — but its identifier and number stay put.
        role.Update(label, request.PluralLabel ?? label, request.Color, request.OrderIndex);

        await db.SaveChangesAsync(cancellationToken);
        await catalog.RefreshAsync(cancellationToken);

        return new SquadRoleDto(role.Value, role.Name, role.Label, role.PluralLabel,
            role.Color, role.OrderIndex, role.IsBuiltIn, role.IsActive, 0);
    }
}

// ---------------------------------------------------------------------------
// Retire / restore
// ---------------------------------------------------------------------------

public sealed record SetRoleActiveCommand(int Value, bool IsActive) : IRequest<SquadRoleDto>;

public sealed class SetRoleActiveCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, IRoleCatalog catalog)
    : IRequestHandler<SetRoleActiveCommand, SquadRoleDto>
{
    public async Task<SquadRoleDto> Handle(SetRoleActiveCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var role = await db.SquadRoles.FirstOrDefaultAsync(r => r.Value == request.Value, cancellationToken)
                   ?? throw new KeyNotFoundException("That role was not found.");

        if (request.IsActive)
        {
            role.Reactivate();
        }
        else
        {
            // Throws for a built-in. Retiring is a soft action on purpose: people already
            // holding the role keep it, and their avatars keep rendering.
            role.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await catalog.RefreshAsync(cancellationToken);

        var inUse = await db.People.CountAsync(p => (int)p.DefaultRole == role.Value, cancellationToken);

        return new SquadRoleDto(role.Value, role.Name, role.Label, role.PluralLabel,
            role.Color, role.OrderIndex, role.IsBuiltIn, role.IsActive, inUse);
    }
}
