using Domain.Common;
using Application.Abstractions;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.People.Profiles;

// ---------------------------------------------------------------------------
// Headline and about
// ---------------------------------------------------------------------------

public sealed record UpdatePersonProfileCommand(Guid PersonId, string? Headline, string? About)
    : IRequest<PersonProfileCardDto>;

public sealed class UpdatePersonProfileCommandValidator
    : AbstractValidator<UpdatePersonProfileCommand>
{
    public UpdatePersonProfileCommandValidator()
    {
        RuleFor(c => c.PersonId).NotEmpty();
        RuleFor(c => c.Headline).MaximumLength(120);
        RuleFor(c => c.About).MaximumLength(2000);
    }
}

public sealed class UpdatePersonProfileCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<UpdatePersonProfileCommand, PersonProfileCardDto>
{
    public async Task<PersonProfileCardDto> Handle(
        UpdatePersonProfileCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await Load(db, request.PersonId, cancellationToken);
        person.UpdateProfile(request.Headline, request.About);

        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }

    internal static async Task<Person> Load(IAppDbContext db, Guid personId,
        CancellationToken cancellationToken) =>
        await db.People
            .Include(p => p.Skills)
            .Include(p => p.Achievements)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
        ?? throw new KeyNotFoundException("That person was not found.");
}

// ---------------------------------------------------------------------------
// Skills
// ---------------------------------------------------------------------------

/// <summary>
/// Adds one or several skills. Several, because people type "Angular, RxJS, FHIR" in one go
/// and splitting that for them is less annoying than telling them not to.
/// </summary>
public sealed record AddPersonSkillsCommand(Guid PersonId, string Names)
    : IRequest<PersonProfileCardDto>;

public sealed class AddPersonSkillsCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<AddPersonSkillsCommand, PersonProfileCardDto>
{
    /// <summary>More than this and it is a CV, not a profile.</summary>
    private const int MaxSkillsPerPerson = 40;

    public async Task<PersonProfileCardDto> Handle(
        AddPersonSkillsCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await UpdatePersonProfileCommandHandler.Load(db, request.PersonId, cancellationToken);

        var names = (request.Names ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length > 0)
            .ToList();

        if (names.Count == 0)
        {
            throw new DomainException("Type a skill first.");
        }

        var before = person.Skills.Select(s => s.Id).ToHashSet();

        foreach (var name in names)
        {
            if (person.Skills.Count >= MaxSkillsPerPerson)
            {
                throw new DomainException(
                    $"A profile can list up to {MaxSkillsPerPerson} skills. Remove one first.");
            }

            person.AddSkill(name);
        }

        // Added to the set explicitly: an untracked child reached through a tracked parent
        // is marked Modified rather than Added, and EF then updates a row that does not
        // exist. Only the genuinely new ones — AddSkill returns the existing row when the
        // skill is already listed.
        foreach (var added in person.Skills.Where(s => !before.Contains(s.Id)))
        {
            db.PersonSkills.Add(added);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }
}

public sealed record RemovePersonSkillCommand(Guid PersonId, Guid SkillId)
    : IRequest<PersonProfileCardDto>;

public sealed class RemovePersonSkillCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<RemovePersonSkillCommand, PersonProfileCardDto>
{
    public async Task<PersonProfileCardDto> Handle(
        RemovePersonSkillCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await UpdatePersonProfileCommandHandler.Load(db, request.PersonId, cancellationToken);
        var skill = person.Skills.FirstOrDefault(s => s.Id == request.SkillId);

        if (skill is not null)
        {
            person.RemoveSkill(request.SkillId);
            db.PersonSkills.Remove(skill);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Achievements
// ---------------------------------------------------------------------------

public sealed record AddPersonAchievementCommand(
    Guid PersonId, string Title, string? Detail, DateOnly? AchievedOn)
    : IRequest<PersonProfileCardDto>;

public sealed class AddPersonAchievementCommandValidator
    : AbstractValidator<AddPersonAchievementCommand>
{
    public AddPersonAchievementCommandValidator()
    {
        RuleFor(c => c.PersonId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Detail).MaximumLength(1000);
    }
}

public sealed class AddPersonAchievementCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<AddPersonAchievementCommand, PersonProfileCardDto>
{
    private const int MaxAchievementsPerPerson = 30;

    public async Task<PersonProfileCardDto> Handle(
        AddPersonAchievementCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await UpdatePersonProfileCommandHandler.Load(db, request.PersonId, cancellationToken);

        if (person.Achievements.Count >= MaxAchievementsPerPerson)
        {
            throw new DomainException(
                $"A profile can list up to {MaxAchievementsPerPerson} achievements.");
        }

        var achievement = person.AddAchievement(request.Title, request.Detail, request.AchievedOn);

        // Explicitly added for the same reason as a skill: a child reached through a
        // tracked parent would otherwise be updated rather than inserted.
        db.PersonAchievements.Add(achievement);

        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }
}

public sealed record RemovePersonAchievementCommand(Guid PersonId, Guid AchievementId)
    : IRequest<PersonProfileCardDto>;

public sealed class RemovePersonAchievementCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<RemovePersonAchievementCommand, PersonProfileCardDto>
{
    public async Task<PersonProfileCardDto> Handle(
        RemovePersonAchievementCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await UpdatePersonProfileCommandHandler.Load(db, request.PersonId, cancellationToken);
        var achievement = person.Achievements.FirstOrDefault(a => a.Id == request.AchievementId);

        if (achievement is not null)
        {
            person.RemoveAchievement(request.AchievementId);
            db.PersonAchievements.Remove(achievement);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Photo
// ---------------------------------------------------------------------------

public sealed record SetPersonPhotoCommand(Guid PersonId, byte[]? Bytes, string? ContentType)
    : IRequest<PersonProfileCardDto>;

public sealed class SetPersonPhotoCommandHandler(
    IAppDbContext db, ICurrentUserContext currentUser, ISender sender)
    : IRequestHandler<SetPersonPhotoCommand, PersonProfileCardDto>
{
    public async Task<PersonProfileCardDto> Handle(
        SetPersonPhotoCommand request, CancellationToken cancellationToken)
    {
        await ProfileEditing.EnsureCanEditAsync(db, currentUser, request.PersonId, cancellationToken);

        var person = await db.People
            .Include(p => p.Photo)
            .FirstOrDefaultAsync(p => p.Id == request.PersonId, cancellationToken)
            ?? throw new KeyNotFoundException("That person was not found.");

        // Replaced rather than updated in place: one row per person, and deleting the old
        // one keeps the unique index honest whichever way EF decides to flush.
        var existing = await db.PersonPhotos
            .FirstOrDefaultAsync(p => p.PersonId == request.PersonId, cancellationToken);

        if (existing is not null) db.PersonPhotos.Remove(existing);

        person.SetPhoto(request.Bytes, request.ContentType);

        if (person.Photo is not null) db.PersonPhotos.Add(person.Photo);

        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetPersonProfileCardQuery(request.PersonId), cancellationToken);
    }
}

/// <summary>The bytes themselves, for the endpoint that serves the image.</summary>
public sealed record GetPersonPhotoQuery(Guid PersonId) : IRequest<PersonPhotoDto?>;

public sealed record PersonPhotoDto(byte[] Bytes, string ContentType, string Version);

public sealed class GetPersonPhotoQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPersonPhotoQuery, PersonPhotoDto?>
{
    public async Task<PersonPhotoDto?> Handle(
        GetPersonPhotoQuery request, CancellationToken cancellationToken)
    {
        var photo = await db.PersonPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PersonId == request.PersonId, cancellationToken);

        return photo is null ? null : new PersonPhotoDto(photo.Bytes, photo.ContentType, photo.Version);
    }
}
