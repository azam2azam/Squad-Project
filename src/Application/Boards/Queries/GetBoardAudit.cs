using Application.Abstractions;
using Application.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Queries;

/// <summary>
/// The board's change log (spec FR-10): who changed status or progress, and when.
/// Newest first, capped so a long-lived board cannot return an unbounded page.
/// </summary>
public sealed record GetBoardAuditQuery(Guid BoardId, int Limit = 50)
    : IRequest<IReadOnlyList<BoardAuditEntryDto>>;

public sealed class GetBoardAuditQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBoardAuditQuery, IReadOnlyList<BoardAuditEntryDto>>
{
    public async Task<IReadOnlyList<BoardAuditEntryDto>> Handle(
        GetBoardAuditQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);

        var entries = await db.BoardAuditEntries
            .Where(e => e.BoardId == request.BoardId)
            .OrderByDescending(e => e.ChangedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entries.Select(BoardAuditEntryDto.From).ToList();
    }
}
