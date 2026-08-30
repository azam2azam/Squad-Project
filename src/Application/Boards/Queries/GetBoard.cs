using Application.Abstractions;
using Application.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Queries;

public sealed record GetBoardQuery(Guid Id) : IRequest<BoardDetailDto>;

public sealed class GetBoardQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBoardQuery, BoardDetailDto>
{
    public async Task<BoardDetailDto> Handle(GetBoardQuery request, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Board {request.Id} was not found.");

        // Composition and warnings are derived on the entity, so the DTO cannot disagree
        // with what the export renderer will compute.
        return BoardDetailDto.From(board);
    }
}
