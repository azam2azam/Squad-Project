using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.People.Profiles;

// ---------------------------------------------------------------------------
// The directory
// ---------------------------------------------------------------------------

/// <summary>
/// The team, as cards you can look through and search.
///
/// The search covers names <em>and</em> skills, because the question this page exists to
/// answer is usually "who here knows X" rather than "where is this person I already had in
/// mind" — the roster screen already does the second one.
/// </summary>
public sealed record GetTeamDirectoryQuery(string? Search = null, bool IncludeInactive = false)
    : IRequest<IReadOnlyList<DirectoryEntryDto>>;

public sealed record DirectoryEntryDto(
    Guid Id,
    string FullName,
    string Initials,
    string RoleLabel,
    string RoleColor,
    string? Headline,
    string? Email,
    IReadOnlyList<string> Skills,
    int AchievementCount,
    bool HasPhoto,
    string? PhotoVersion,
    bool IsActive,
    int BoardCount);

public sealed class GetTeamDirectoryQueryHandler(IAppDbContext db)
    : IRequestHandler<GetTeamDirectoryQuery, IReadOnlyList<DirectoryEntryDto>>
{
    public async Task<IReadOnlyList<DirectoryEntryDto>> Handle(
        GetTeamDirectoryQuery request, CancellationToken cancellationToken)
    {
        var term = request.Search?.Trim();

        var query = db.People.AsNoTracking();

        if (!request.IncludeInactive) query = query.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(p =>
                EF.Functions.Like(p.FullName, $"%{term}%")
                || (p.Headline != null && EF.Functions.Like(p.Headline, $"%{term}%"))
                || db.PersonSkills.Any(s => s.PersonId == p.Id && EF.Functions.Like(s.Name, $"%{term}%")));
        }

        // Projected rather than loaded: this page shows forty people, and the photo bytes
        // are the one thing on a profile big enough to matter.
        var rows = await query
            .OrderBy(p => p.FullName)
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.DefaultRole,
                p.Headline,
                p.Email,
                p.IsActive,
                p.AvatarColorOverride,
                Skills = db.PersonSkills
                    .Where(s => s.PersonId == p.Id)
                    .OrderBy(s => s.OrderIndex)
                    .Select(s => s.Name)
                    .ToList(),
                AchievementCount = db.PersonAchievements.Count(a => a.PersonId == p.Id),
                Photo = db.PersonPhotos
                    .Where(ph => ph.PersonId == p.Id)
                    .Select(ph => (DateTimeOffset?)ph.UpdatedAt)
                    .FirstOrDefault(),
                BoardCount = db.SquadMembers.Count(m => m.PersonId == p.Id)
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DirectoryEntryDto(
            r.Id,
            r.FullName,
            Person.ComputeInitials(r.FullName),
            RoleMetadata.Label(r.DefaultRole),
            r.AvatarColorOverride ?? RoleMetadata.Color(r.DefaultRole),
            r.Headline,
            r.Email,
            r.Skills,
            r.AchievementCount,
            r.Photo is not null,
            r.Photo?.ToUnixTimeSeconds().ToString(),
            r.IsActive,
            r.BoardCount)).ToList();
    }
}

// ---------------------------------------------------------------------------
// One profile
// ---------------------------------------------------------------------------

public sealed record GetPersonProfileCardQuery(Guid PersonId) : IRequest<PersonProfileCardDto>;

public sealed record PersonProfileCardDto(
    Guid Id,
    string FullName,
    string Initials,
    string RoleLabel,
    string RoleColor,
    string? Headline,
    string? About,
    string? Email,
    bool IsActive,
    bool HasPhoto,
    string? PhotoVersion,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<AchievementDto> Achievements,
    /// <summary>True when the caller may edit this profile — their own, or they are an admin.</summary>
    bool CanEdit);

public sealed record SkillDto(Guid Id, string Name);

public sealed record AchievementDto(Guid Id, string Title, string? Detail, DateOnly? AchievedOn);

public sealed class GetPersonProfileCardQueryHandler(
    IAppDbContext db, ICurrentUserContext currentUser)
    : IRequestHandler<GetPersonProfileCardQuery, PersonProfileCardDto>
{
    public async Task<PersonProfileCardDto> Handle(
        GetPersonProfileCardQuery request, CancellationToken cancellationToken)
    {
        var person = await db.People
            .AsNoTracking()
            .Include(p => p.Skills)
            .Include(p => p.Achievements)
            .FirstOrDefaultAsync(p => p.Id == request.PersonId, cancellationToken)
            ?? throw new KeyNotFoundException("That person was not found.");

        var photo = await db.PersonPhotos
            .AsNoTracking()
            .Where(p => p.PersonId == person.Id)
            .Select(p => (DateTimeOffset?)p.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new PersonProfileCardDto(
            person.Id,
            person.FullName,
            person.Initials,
            RoleMetadata.Label(person.DefaultRole),
            person.AvatarColorOverride ?? RoleMetadata.Color(person.DefaultRole),
            person.Headline,
            person.About,
            person.Email,
            person.IsActive,
            photo is not null,
            photo?.ToUnixTimeSeconds().ToString(),
            person.Skills.OrderBy(s => s.OrderIndex).Select(s => new SkillDto(s.Id, s.Name)).ToList(),
            person.Achievements
                .OrderBy(a => a.OrderIndex)
                .Select(a => new AchievementDto(a.Id, a.Title, a.Detail, a.AchievedOn))
                .ToList(),
            await ProfileEditing.CanEditAsync(db, currentUser, person.Id, cancellationToken));
    }
}

/// <summary>
/// Who may write to a profile.
///
/// A person edits their own, an administrator edits anybody's, and nobody else edits
/// anything. "Their own" is decided by the link between an application account and a roster
/// entry — the same link the person profile already uses — so somebody with no account, or
/// an account not pointed at a roster entry, simply has no profile to edit and is told so
/// rather than silently refused.
/// </summary>
public static class ProfileEditing
{
    public static async Task<bool> CanEditAsync(IAppDbContext db, ICurrentUserContext currentUser,
        Guid personId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) return false;
        if (currentUser.Role is UserRole.Admin) return true;

        // A Viewer account is read-only everywhere else in the application; their own
        // profile is the one thing they own, so it is deliberately not excluded here.
        return await db.Users.AnyAsync(
            u => u.Id == userId && u.PersonId == personId, cancellationToken);
    }

    public static async Task EnsureCanEditAsync(IAppDbContext db, ICurrentUserContext currentUser,
        Guid personId, CancellationToken cancellationToken)
    {
        if (await CanEditAsync(db, currentUser, personId, cancellationToken)) return;

        throw new ForbiddenException(
            "You can only edit your own profile. An administrator can edit anyone's, and can "
            + "link your account to your roster entry if it is not linked yet.");
    }
}
