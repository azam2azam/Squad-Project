using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Categories;

/// <summary>
/// Programmes above the boards — VIDA 4, AI, and so on.
///
/// Reading is open to anyone signed in, because every picker and filter needs the list.
/// Writing is admin-only: a category regroups the whole portfolio, so it is org-wide
/// reference data rather than something a board owner changes for themselves.
/// </summary>
public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    int OrderIndex,
    bool IsActive,
    int BoardCount);

// ---------------------------------------------------------------------------
// Read
// ---------------------------------------------------------------------------

public sealed record ListCategoriesQuery(bool IncludeInactive = false)
    : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class ListCategoriesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(
        ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await db.BoardCategories
            .Where(c => request.IncludeInactive || c.IsActive)
            .OrderBy(c => c.OrderIndex)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // How many boards sit in each, so retiring one is an informed decision.
        var counts = await db.Boards
            .Where(b => b.CategoryId != null)
            .GroupBy(b => b.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return categories
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Description, c.Color, c.OrderIndex, c.IsActive,
                counts.TryGetValue(c.Id, out var n) ? n : 0))
            .ToList();
    }
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public sealed record CreateCategoryCommand(string Name, string? Description, string Color)
    : IRequest<CategoryDto>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Description).MaximumLength(400);
        RuleFor(c => c.Color).NotEmpty();
    }
}

public sealed class CreateCategoryCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(
        CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var name = request.Name.Trim();

        if (await db.BoardCategories.AnyAsync(c => c.Name == name, cancellationToken))
        {
            throw new DomainException($"A category called {name} already exists.");
        }

        var highest = await db.BoardCategories
            .Select(c => (int?)c.OrderIndex)
            .MaxAsync(cancellationToken) ?? -1;

        var category = new BoardCategory(name, request.Description, request.Color, highest + 1);

        db.BoardCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return new CategoryDto(category.Id, category.Name, category.Description,
            category.Color, category.OrderIndex, category.IsActive, 0);
    }
}

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

public sealed record UpdateCategoryCommand(
    Guid Id, string Name, string? Description, string Color, int OrderIndex)
    : IRequest<CategoryDto>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Description).MaximumLength(400);
        RuleFor(c => c.Color).NotEmpty();
    }
}

public sealed class UpdateCategoryCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(
        UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var category = await db.BoardCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("That category was not found.");

        var name = request.Name.Trim();

        if (await db.BoardCategories.AnyAsync(
                c => c.Name == name && c.Id != request.Id, cancellationToken))
        {
            throw new DomainException($"A category called {name} already exists.");
        }

        category.Update(name, request.Description, request.Color, request.OrderIndex);
        await db.SaveChangesAsync(cancellationToken);

        var boardCount = await db.Boards.CountAsync(b => b.CategoryId == category.Id, cancellationToken);

        return new CategoryDto(category.Id, category.Name, category.Description,
            category.Color, category.OrderIndex, category.IsActive, boardCount);
    }
}

// ---------------------------------------------------------------------------
// Retire / restore
// ---------------------------------------------------------------------------

public sealed record SetCategoryActiveCommand(Guid Id, bool IsActive) : IRequest<CategoryDto>;

public sealed class SetCategoryActiveCommandHandler(IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<SetCategoryActiveCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(
        SetCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var category = await db.BoardCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("That category was not found.");

        if (request.IsActive)
        {
            category.Reactivate();
        }
        else
        {
            // Soft: boards keep their grouping. Retiring a programme must not silently
            // tip its boards back into the uncategorised pile.
            category.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);

        var boardCount = await db.Boards.CountAsync(b => b.CategoryId == category.Id, cancellationToken);

        return new CategoryDto(category.Id, category.Name, category.Description,
            category.Color, category.OrderIndex, category.IsActive, boardCount);
    }
}

// ---------------------------------------------------------------------------
// Bulk assignment
// ---------------------------------------------------------------------------

/// <summary>
/// Moves many boards into a programme at once, or out of one when the category is null.
///
/// Exists because the alternative is opening sixteen boards one at a time: a portfolio
/// gets categorised in one sitting, not gradually.
/// </summary>
public sealed record AssignBoardsToCategoryCommand(IReadOnlyList<Guid> BoardIds, Guid? CategoryId)
    : IRequest<int>;

public sealed class AssignBoardsToCategoryCommandHandler(
    IAppDbContext db, IBoardAuthorizer authorizer)
    : IRequestHandler<AssignBoardsToCategoryCommand, int>
{
    public async Task<int> Handle(
        AssignBoardsToCategoryCommand request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        if (request.CategoryId is { } categoryId
            && !await db.BoardCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new DomainException("That category no longer exists.");
        }

        var boards = await db.Boards
            .Where(b => request.BoardIds.Contains(b.Id))
            .ToListAsync(cancellationToken);

        foreach (var board in boards)
        {
            board.AssignCategory(request.CategoryId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return boards.Count;
    }
}
