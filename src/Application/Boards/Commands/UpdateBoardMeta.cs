using Application.Abstractions;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Commands;

public sealed record UpdateBoardMetaCommand(
    Guid Id,
    string Title,
    string Product,
    string SquadName,
    string? Sprint,
    BoardStatus Status,
    int ProgressPercent,
    string? BlockerNote = null,
    double? Velocity = null,
    DateOnly? TargetDate = null,
    string? JiraProjectKey = null,
    string? JiraBoardId = null) : IRequest<BoardDetailDto>;

public sealed class UpdateBoardMetaCommandValidator : AbstractValidator<UpdateBoardMetaCommand>
{
    public UpdateBoardMetaCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Product).NotEmpty().MaximumLength(100);
        RuleFor(c => c.SquadName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Sprint).MaximumLength(100);
        RuleFor(c => c.BlockerNote).MaximumLength(1000);
        RuleFor(c => c.ProgressPercent).InclusiveBetween(0, 100);
        RuleFor(c => c.Velocity).GreaterThanOrEqualTo(0).When(c => c.Velocity.HasValue);
        RuleFor(c => c.Status).IsInEnum();
    }
}

public sealed class UpdateBoardMetaCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IBoardNotifier notifier)
    : IRequestHandler<UpdateBoardMetaCommand, BoardDetailDto>
{
    public async Task<BoardDetailDto> Handle(
        UpdateBoardMetaCommand request, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.Id} was not found.");

        // Captured before mutation so the audit log records the actual transition.
        var previousStatus = board.Status;
        var previousProgress = board.ProgressPercent;

        board.UpdateMeta(
            request.Title, request.Product, request.SquadName, request.Sprint,
            request.Status, request.ProgressPercent, request.BlockerNote,
            request.Velocity, request.TargetDate, request.JiraProjectKey, request.JiraBoardId);

        // Status and progress are the two fields reviewers ask "who changed this?" about.
        if (previousStatus != board.Status)
        {
            db.BoardAuditEntries.Add(new BoardAuditEntry(board.Id, "Status",
                BoardStatusMetadata.Label(previousStatus),
                BoardStatusMetadata.Label(board.Status),
                currentUser.DisplayName));
        }

        if (previousProgress != board.ProgressPercent)
        {
            db.BoardAuditEntries.Add(new BoardAuditEntry(board.Id, "Progress",
                $"{previousProgress}%", $"{board.ProgressPercent}%", currentUser.DisplayName));
        }

        await db.SaveChangesAsync(cancellationToken);

        var dto = BoardDetailDto.From(board);
        await notifier.BoardUpdatedAsync(board.Id, dto, cancellationToken);

        return dto;
    }
}
