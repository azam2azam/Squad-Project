using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Commands;

public sealed record CreateBoardCommand(
    string Title,
    string Product,
    string SquadName,
    string? Sprint,
    BoardStatus Status,
    int ProgressPercent) : IRequest<BoardDetailDto>;

public sealed class CreateBoardCommandValidator : AbstractValidator<CreateBoardCommand>
{
    public CreateBoardCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Product).NotEmpty().MaximumLength(100);
        RuleFor(c => c.SquadName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Sprint).MaximumLength(100);
        RuleFor(c => c.ProgressPercent).InclusiveBetween(0, 100);
        RuleFor(c => c.Status).IsInEnum();
    }
}

public sealed class CreateBoardCommandHandler(
    IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CreateBoardCommand, BoardDetailDto>
{
    public async Task<BoardDetailDto> Handle(
        CreateBoardCommand request, CancellationToken cancellationToken)
    {
        // New boards go to the end of the portfolio rather than displacing existing ones.
        var nextOrder = await db.Boards.AnyAsync(cancellationToken)
            ? await db.Boards.MaxAsync(b => b.OrderIndex, cancellationToken) + 1
            : 0;

        var board = new Board(
            request.Title,
            request.Product,
            request.SquadName,
            request.Sprint,
            request.Status,
            request.ProgressPercent,
            currentUser.DisplayName,
            nextOrder);

        db.Boards.Add(board);
        db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Board", null, "Created", currentUser.DisplayName));

        await db.SaveChangesAsync(cancellationToken);

        return BoardDetailDto.From(board);
    }
}
