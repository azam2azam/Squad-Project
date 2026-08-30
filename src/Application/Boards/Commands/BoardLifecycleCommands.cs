using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Commands;

// ---------------------------------------------------------------------------
// Duplicate
// ---------------------------------------------------------------------------

public sealed record DuplicateBoardCommand(Guid Id, string? NewTitle = null)
    : IRequest<BoardDetailDto>;

public sealed class DuplicateBoardCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DuplicateBoardCommand, BoardDetailDto>
{
    public async Task<BoardDetailDto> Handle(
        DuplicateBoardCommand request, CancellationToken cancellationToken)
    {
        var source = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.Id} was not found.");

        var copy = source.Duplicate(currentUser.DisplayName, request.NewTitle);

        db.Boards.Add(copy);
        db.BoardAuditEntries.Add(new BoardAuditEntry(
            copy.Id, "Board", null, $"Duplicated from '{source.Title}'", currentUser.DisplayName));

        await db.SaveChangesAsync(cancellationToken);

        return BoardDetailDto.From(copy);
    }
}

// ---------------------------------------------------------------------------
// Soft delete
// ---------------------------------------------------------------------------

public sealed record DeleteBoardCommand(Guid Id) : IRequest;

public sealed class DeleteBoardCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IBoardNotifier notifier)
    : IRequestHandler<DeleteBoardCommand>
{
    public async Task Handle(DeleteBoardCommand request, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.Id} was not found.");

        board.SoftDelete();

        db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Board", "Active", "Deleted", currentUser.DisplayName));

        await db.SaveChangesAsync(cancellationToken);
        await notifier.BoardUpdatedAsync(board.Id, new { deleted = true }, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Reorder
// ---------------------------------------------------------------------------

public sealed record BoardOrderItem(Guid Id, int OrderIndex);

public sealed record ReorderBoardsCommand(IReadOnlyList<BoardOrderItem> Items) : IRequest;

public sealed class ReorderBoardsCommandValidator : AbstractValidator<ReorderBoardsCommand>
{
    public ReorderBoardsCommandValidator()
    {
        RuleFor(c => c.Items).NotEmpty();
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty();
            item.RuleFor(i => i.OrderIndex).GreaterThanOrEqualTo(0);
        });
        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count)
            .WithMessage("Each board may appear only once in a reorder request.");
    }
}

public sealed class ReorderBoardsCommandHandler(IAppDbContext db)
    : IRequestHandler<ReorderBoardsCommand>
{
    public async Task Handle(ReorderBoardsCommand request, CancellationToken cancellationToken)
    {
        var ids = request.Items.Select(i => i.Id).ToList();

        var boards = await db.Boards
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(cancellationToken);

        var missing = ids.Except(boards.Select(b => b.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new KeyNotFoundException(
                $"Board(s) not found: {string.Join(", ", missing)}");
        }

        foreach (var board in boards)
        {
            board.SetOrder(request.Items.First(i => i.Id == board.Id).OrderIndex);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
