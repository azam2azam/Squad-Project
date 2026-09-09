using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Staff;

// ---------------------------------------------------------------------------
// Work items
// ---------------------------------------------------------------------------

/// <summary>
/// Work items are edited by whoever may edit the board they sit on, not by admins only:
/// the person who knows a task is finished is the one working it.
/// </summary>
public sealed record ListWorkItemsQuery(Guid? BoardId, Guid? PersonId, bool IncludeDone = true)
    : IRequest<IReadOnlyList<WorkItemDto>>;

public sealed class ListWorkItemsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListWorkItemsQuery, IReadOnlyList<WorkItemDto>>
{
    public async Task<IReadOnlyList<WorkItemDto>> Handle(
        ListWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var query = db.WorkItems.Include(w => w.Board).Include(w => w.Person).AsQueryable();

        if (request.BoardId is { } boardId) query = query.Where(w => w.BoardId == boardId);
        if (request.PersonId is { } personId) query = query.Where(w => w.PersonId == personId);
        if (!request.IncludeDone) query = query.Where(w => w.Status != WorkItemStatus.Done);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = await query
            .OrderBy(w => w.Status == WorkItemStatus.Done)
            .ThenBy(w => w.PlannedEnd)
            .ThenBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return items.Select(w => GetPersonProfileQueryHandler.Map(w, today)).ToList();
    }
}

public sealed record CreateWorkItemCommand(
    Guid BoardId,
    string Title,
    Guid? PersonId,
    WorkItemStatus Status,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    double? EffortHours,
    string? Detail) : IRequest<WorkItemDto>;

public sealed class CreateWorkItemCommandValidator : AbstractValidator<CreateWorkItemCommand>
{
    public CreateWorkItemCommandValidator()
    {
        RuleFor(c => c.BoardId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(300);
        RuleFor(c => c.Detail).MaximumLength(2000);
    }
}

public sealed class CreateWorkItemCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, ICurrentUser currentUser)
    : IRequestHandler<CreateWorkItemCommand, WorkItemDto>
{
    public async Task<WorkItemDto> Handle(
        CreateWorkItemCommand request, CancellationToken cancellationToken)
    {
        await authorizer.EnsureCanEditAsync(request.BoardId, cancellationToken);

        if (request.PersonId is { } personId
            && !await db.People.AnyAsync(p => p.Id == personId, cancellationToken))
        {
            throw new DomainException("That roster member no longer exists.");
        }

        var item = new WorkItem(request.BoardId, request.Title, request.PersonId,
            request.Status, request.PlannedStart, request.PlannedEnd, request.EffortHours,
            request.Detail, currentUser.DisplayName);

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(db, item.Id, cancellationToken);
    }

    /// <summary>Reloaded so the DTO carries board and person names rather than blanks.</summary>
    internal static async Task<WorkItemDto> ReloadAsync(
        IAppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var saved = await db.WorkItems
            .Include(w => w.Board)
            .Include(w => w.Person)
            .FirstAsync(w => w.Id == id, cancellationToken);

        return GetPersonProfileQueryHandler.Map(saved, DateOnly.FromDateTime(DateTime.UtcNow));
    }
}

public sealed record UpdateWorkItemCommand(
    Guid Id,
    string Title,
    Guid? PersonId,
    WorkItemStatus Status,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    double? EffortHours,
    string? Detail) : IRequest<WorkItemDto>;

public sealed class UpdateWorkItemCommandValidator : AbstractValidator<UpdateWorkItemCommand>
{
    public UpdateWorkItemCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(300);
        RuleFor(c => c.Detail).MaximumLength(2000);
    }
}

public sealed class UpdateWorkItemCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<UpdateWorkItemCommand, WorkItemDto>
{
    public async Task<WorkItemDto> Handle(
        UpdateWorkItemCommand request, CancellationToken cancellationToken)
    {
        var item = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That work item was not found.");

        await authorizer.EnsureCanEditAsync(item.BoardId, cancellationToken);

        if (request.PersonId is { } personId
            && !await db.People.AnyAsync(p => p.Id == personId, cancellationToken))
        {
            throw new DomainException("That roster member no longer exists.");
        }

        item.Update(request.Title, request.PersonId, request.Status,
            request.PlannedStart, request.PlannedEnd, request.EffortHours, request.Detail);

        await db.SaveChangesAsync(cancellationToken);

        return await CreateWorkItemCommandHandler.ReloadAsync(db, item.Id, cancellationToken);
    }
}

public sealed record DeleteWorkItemCommand(Guid Id) : IRequest;

public sealed class DeleteWorkItemCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<DeleteWorkItemCommand>
{
    public async Task Handle(DeleteWorkItemCommand request, CancellationToken cancellationToken)
    {
        var item = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
                   ?? throw new KeyNotFoundException("That work item was not found.");

        await authorizer.EnsureCanEditAsync(item.BoardId, cancellationToken);

        // A hard delete: unlike a board, a task carries no history worth preserving, and a
        // list quietly full of deleted rows is worse than one that is simply shorter.
        db.WorkItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Availability
// ---------------------------------------------------------------------------

/// <summary>
/// Recording somebody's leave is a roster-level act, so it takes the same admin gate as
/// the roster itself.
/// </summary>
public sealed record SaveAvailabilityCommand(
    Guid? Id,
    Guid PersonId,
    DateOnly FromDate,
    DateOnly ToDate,
    AvailabilityKind Kind,
    int CapacityPercent,
    string? Note) : IRequest<AvailabilityDto>;

public sealed class SaveAvailabilityCommandValidator : AbstractValidator<SaveAvailabilityCommand>
{
    public SaveAvailabilityCommandValidator()
    {
        RuleFor(c => c.PersonId).NotEmpty();
        RuleFor(c => c.CapacityPercent).InclusiveBetween(0, 100);
        RuleFor(c => c.Note).MaximumLength(400);
    }
}

public sealed class SaveAvailabilityCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer, ICurrentUser currentUser)
    : IRequestHandler<SaveAvailabilityCommand, AvailabilityDto>
{
    public async Task<AvailabilityDto> Handle(
        SaveAvailabilityCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        if (!await db.People.AnyAsync(p => p.Id == request.PersonId, cancellationToken))
        {
            throw new DomainException("That roster member no longer exists.");
        }

        PersonAvailability record;

        if (request.Id is { } id)
        {
            record = await db.PersonAvailability
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("That availability record was not found.");

            record.Update(request.FromDate, request.ToDate, request.Kind,
                request.CapacityPercent, request.Note);
        }
        else
        {
            record = new PersonAvailability(request.PersonId, request.FromDate, request.ToDate,
                request.Kind, request.CapacityPercent, request.Note, currentUser.DisplayName);

            db.PersonAvailability.Add(record);
        }

        await db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new AvailabilityDto(record.Id, record.FromDate, record.ToDate,
            (int)record.Kind, StaffMetadata.Label(record.Kind),
            StaffMetadata.Color(record.Kind), record.CapacityPercent, record.Note,
            record.Covers(today));
    }
}

public sealed record DeleteAvailabilityCommand(Guid Id) : IRequest;

public sealed class DeleteAvailabilityCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<DeleteAvailabilityCommand>
{
    public async Task Handle(DeleteAvailabilityCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var record = await db.PersonAvailability
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("That availability record was not found.");

        db.PersonAvailability.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Assignment scheduling
// ---------------------------------------------------------------------------

/// <summary>
/// Puts dates and an allocation on an existing squad assignment — the piece that turns a
/// membership list into a schedule.
/// </summary>
public sealed record ScheduleAssignmentCommand(
    Guid MemberId,
    int? AllocationPercent,
    DateOnly? StartsOn,
    DateOnly? EndsOn) : IRequest<AssignmentDto>;

public sealed class ScheduleAssignmentCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<ScheduleAssignmentCommand, AssignmentDto>
{
    public async Task<AssignmentDto> Handle(
        ScheduleAssignmentCommand request, CancellationToken cancellationToken)
    {
        var member = await db.SquadMembers
            .Include(m => m.Board)
            .FirstOrDefaultAsync(m => m.Id == request.MemberId, cancellationToken)
            ?? throw new KeyNotFoundException("That assignment was not found.");

        await authorizer.EnsureCanEditAsync(member.BoardId, cancellationToken);

        member.Update(member.Role, member.Detail, request.AllocationPercent);
        member.Schedule(request.StartsOn, request.EndsOn);

        await db.SaveChangesAsync(cancellationToken);

        return new AssignmentDto(
            member.BoardId, member.Board.Title, member.Board.SquadName,
            RoleMetadata.Label(member.Role), RoleMetadata.Color(member.Role),
            member.AllocationPercent, member.StartsOn, member.EndsOn);
    }
}
