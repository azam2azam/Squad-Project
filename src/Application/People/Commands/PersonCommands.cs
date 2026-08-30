using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.People.Commands;

internal static class PersonRules
{
    /// <summary>Accepts #RGB, #RRGGBB and #RRGGBBAA.</summary>
    public const string HexColor = "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$";

    public const string HexColorMessage = "Avatar colour must be a hex value such as #2DD4BF.";
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public sealed record CreatePersonCommand(
    string FullName,
    Role DefaultRole,
    string? DefaultDetail = null,
    string? Email = null,
    string? AvatarColorOverride = null) : IRequest<PersonDto>;

public sealed class CreatePersonCommandValidator : AbstractValidator<CreatePersonCommand>
{
    public CreatePersonCommandValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DefaultRole).IsInEnum();
        RuleFor(c => c.DefaultDetail).MaximumLength(200);
        RuleFor(c => c.Email).MaximumLength(320).EmailAddress()
            .When(c => !string.IsNullOrWhiteSpace(c.Email));
        RuleFor(c => c.AvatarColorOverride).Matches(PersonRules.HexColor)
            .When(c => !string.IsNullOrWhiteSpace(c.AvatarColorOverride))
            .WithMessage(PersonRules.HexColorMessage);
    }
}

public sealed class CreatePersonCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<CreatePersonCommand, PersonDto>
{
    public async Task<PersonDto> Handle(CreatePersonCommand request, CancellationToken cancellationToken)
    {
        // The roster is org-wide, so a PO editing it would affect every squad.
        authorizer.EnsureIsAdmin();

        var person = new Person(request.FullName, request.DefaultRole,
            request.DefaultDetail, request.Email, request.AvatarColorOverride);

        db.People.Add(person);
        await db.SaveChangesAsync(cancellationToken);

        return PersonDto.From(person);
    }
}

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

public sealed record UpdatePersonCommand(
    Guid Id,
    string FullName,
    Role DefaultRole,
    string? DefaultDetail = null,
    string? Email = null,
    string? AvatarColorOverride = null) : IRequest<PersonDto>;

public sealed class UpdatePersonCommandValidator : AbstractValidator<UpdatePersonCommand>
{
    public UpdatePersonCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DefaultRole).IsInEnum();
        RuleFor(c => c.DefaultDetail).MaximumLength(200);
        RuleFor(c => c.Email).MaximumLength(320).EmailAddress()
            .When(c => !string.IsNullOrWhiteSpace(c.Email));
        RuleFor(c => c.AvatarColorOverride).Matches(PersonRules.HexColor)
            .When(c => !string.IsNullOrWhiteSpace(c.AvatarColorOverride))
            .WithMessage(PersonRules.HexColorMessage);
    }
}

public sealed class UpdatePersonCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<UpdatePersonCommand, PersonDto>
{
    public async Task<PersonDto> Handle(UpdatePersonCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var person = await db.People
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Person {request.Id} was not found.");

        person.Update(request.FullName, request.DefaultRole, request.DefaultDetail,
            request.Email, request.AvatarColorOverride);

        await db.SaveChangesAsync(cancellationToken);

        return PersonDto.From(person);
    }
}

// ---------------------------------------------------------------------------
// Deactivate / reactivate — deletion is always soft
// ---------------------------------------------------------------------------

public sealed record DeactivatePersonCommand(Guid Id) : IRequest;

public sealed class DeactivatePersonCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<DeactivatePersonCommand>
{
    public async Task Handle(DeactivatePersonCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var person = await db.People
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Person {request.Id} was not found.");

        // Squad assignments are deliberately left in place: a board is a historical
        // snapshot and must still show who was on it (spec section 5).
        person.Deactivate();

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ReactivatePersonCommand(Guid Id) : IRequest<PersonDto>;

public sealed class ReactivatePersonCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<ReactivatePersonCommand, PersonDto>
{
    public async Task<PersonDto> Handle(ReactivatePersonCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var person = await db.People
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Person {request.Id} was not found.");

        person.Reactivate();
        await db.SaveChangesAsync(cancellationToken);

        return PersonDto.From(person);
    }
}
