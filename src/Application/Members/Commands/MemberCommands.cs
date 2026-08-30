using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Members.Commands;

// ---------------------------------------------------------------------------
// Add
// ---------------------------------------------------------------------------

/// <summary>
/// Adds a person to a board's squad. Either <paramref name="PersonId"/> names an
/// existing roster member, or <paramref name="NewPerson"/> quick-creates one inline —
/// the roster stays the source of truth either way, so a name typed here is still
/// reusable on the next board.
/// </summary>
public sealed record AddMemberCommand(
    Guid BoardId,
    Guid? PersonId,
    NewPersonInput? NewPerson,
    Role Role,
    string? Detail = null,
    int? AllocationPercent = null) : IRequest<SquadMemberDto>;

public sealed record NewPersonInput(
    string FullName,
    Role DefaultRole,
    string? DefaultDetail = null,
    string? Email = null);

public sealed class AddMemberCommandValidator : AbstractValidator<AddMemberCommand>
{
    public AddMemberCommandValidator()
    {
        RuleFor(c => c.BoardId).NotEmpty();
        RuleFor(c => c.Role).IsInEnum();
        RuleFor(c => c.Detail).MaximumLength(200);
        RuleFor(c => c.AllocationPercent).InclusiveBetween(0, 100)
            .When(c => c.AllocationPercent.HasValue);

        RuleFor(c => c)
            .Must(c => c.PersonId.HasValue ^ c.NewPerson is not null)
            .WithMessage("Supply either an existing personId or a new person, not both.");

        When(c => c.NewPerson is not null, () =>
        {
            RuleFor(c => c.NewPerson!.FullName).NotEmpty().MaximumLength(200);
            RuleFor(c => c.NewPerson!.DefaultRole).IsInEnum();
            RuleFor(c => c.NewPerson!.DefaultDetail).MaximumLength(200);
        });
    }
}

public sealed class AddMemberCommandHandler(
    IAppDbContext db, IBoardNotifier notifier, IBoardAuthorizer authorizer)
    : IRequestHandler<AddMemberCommand, SquadMemberDto>
{
    public async Task<SquadMemberDto> Handle(AddMemberCommand request, CancellationToken cancellationToken)
    {
        await authorizer.EnsureCanEditAsync(request.BoardId, cancellationToken);

        var board = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.BoardId} was not found.");

        Person person;
        if (request.PersonId.HasValue)
        {
            person = await db.People
                .FirstOrDefaultAsync(p => p.Id == request.PersonId.Value, cancellationToken)
                ?? throw new KeyNotFoundException($"Person {request.PersonId} was not found.");
        }
        else
        {
            var input = request.NewPerson!;
            person = new Person(input.FullName, input.DefaultRole, input.DefaultDetail, input.Email);
            db.People.Add(person);
        }

        // Duplicate and inactive checks live on the aggregate, so they hold no matter
        // which path created the person.
        var member = board.AddMember(person, request.Role, request.Detail, request.AllocationPercent);

        // Tracked explicitly. Entity assigns its own Guid key in the constructor, and EF
        // marks an untracked entity reached through a tracked parent's navigation as
        // Modified rather than Added when its key is already set — which would emit an
        // UPDATE against a row that does not exist yet.
        db.SquadMembers.Add(member);

        await db.SaveChangesAsync(cancellationToken);

        var dto = SquadMemberDto.From(member);
        await notifier.MemberChangedAsync(board.Id, dto, cancellationToken);

        return dto;
    }
}

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

public sealed record UpdateMemberCommand(
    Guid Id,
    Role Role,
    string? Detail = null,
    int? AllocationPercent = null) : IRequest<SquadMemberDto>;

public sealed class UpdateMemberCommandValidator : AbstractValidator<UpdateMemberCommand>
{
    public UpdateMemberCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Role).IsInEnum();
        RuleFor(c => c.Detail).MaximumLength(200);
        RuleFor(c => c.AllocationPercent).InclusiveBetween(0, 100)
            .When(c => c.AllocationPercent.HasValue);
    }
}

public sealed class UpdateMemberCommandHandler(
    IAppDbContext db, IBoardNotifier notifier, IBoardAuthorizer authorizer)
    : IRequestHandler<UpdateMemberCommand, SquadMemberDto>
{
    public async Task<SquadMemberDto> Handle(UpdateMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await db.SquadMembers
            .Include(m => m.Person)
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Squad member {request.Id} was not found.");

        // The route addresses a member, but permission belongs to its board.
        await authorizer.EnsureCanEditAsync(member.BoardId, cancellationToken);

        member.Update(request.Role, request.Detail, request.AllocationPercent);

        await db.SaveChangesAsync(cancellationToken);

        var dto = SquadMemberDto.From(member);
        await notifier.MemberChangedAsync(member.BoardId, dto, cancellationToken);

        return dto;
    }
}

// ---------------------------------------------------------------------------
// Remove
// ---------------------------------------------------------------------------

public sealed record RemoveMemberCommand(Guid Id) : IRequest;

public sealed class RemoveMemberCommandHandler(
    IAppDbContext db, IBoardNotifier notifier, IBoardAuthorizer authorizer)
    : IRequestHandler<RemoveMemberCommand>
{
    public async Task Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await db.SquadMembers
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Squad member {request.Id} was not found.");

        var boardId = member.BoardId;

        // The route addresses a member, but permission belongs to its board.
        await authorizer.EnsureCanEditAsync(boardId, cancellationToken);

        var board = await db.Boards
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {boardId} was not found.");

        // Goes through the aggregate so the remaining members are resequenced.
        board.RemoveMember(request.Id);
        db.SquadMembers.Remove(member);

        await db.SaveChangesAsync(cancellationToken);
        await notifier.MemberChangedAsync(boardId, new { removed = request.Id }, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Reorder
// ---------------------------------------------------------------------------

public sealed record ReorderMembersCommand(Guid BoardId, IReadOnlyList<Guid> OrderedMemberIds)
    : IRequest;

public sealed class ReorderMembersCommandValidator : AbstractValidator<ReorderMembersCommand>
{
    public ReorderMembersCommandValidator()
    {
        RuleFor(c => c.BoardId).NotEmpty();
        RuleFor(c => c.OrderedMemberIds).NotEmpty();
        RuleFor(c => c.OrderedMemberIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Each member may appear only once in a reorder request.");
    }
}

public sealed class ReorderMembersCommandHandler(
    IAppDbContext db, IBoardNotifier notifier, IBoardAuthorizer authorizer)
    : IRequestHandler<ReorderMembersCommand>
{
    public async Task Handle(ReorderMembersCommand request, CancellationToken cancellationToken)
    {
        await authorizer.EnsureCanEditAsync(request.BoardId, cancellationToken);

        var board = await db.Boards
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.BoardId} was not found.");

        var unknown = request.OrderedMemberIds
            .Except(board.Members.Select(m => m.Id))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new KeyNotFoundException(
                $"Member(s) not on this board: {string.Join(", ", unknown)}");
        }

        board.ReorderMembers(request.OrderedMemberIds);

        await db.SaveChangesAsync(cancellationToken);
        await notifier.MemberChangedAsync(board.Id, new { reordered = true }, cancellationToken);
    }
}
