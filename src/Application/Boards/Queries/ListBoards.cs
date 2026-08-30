using Application.Abstractions;
using Application.Common;
using Application.Contracts;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Boards.Queries;

/// <summary>Portfolio listing. Optional free-text and status filters for the exec view.</summary>
public sealed record ListBoardsQuery(
    string? Search = null,
    BoardStatus? Status = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<BoardSummaryDto>>;

public sealed class ListBoardsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBoardsQuery, PagedResult<BoardSummaryDto>>
{
    public async Task<PagedResult<BoardSummaryDto>> Handle(
        ListBoardsQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageQuery { Page = request.Page, PageSize = request.PageSize };

        var query = db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.Title, $"%{term}%") ||
                EF.Functions.Like(b.Product, $"%{term}%") ||
                EF.Functions.Like(b.SquadName, $"%{term}%"));
        }

        if (request.Status.HasValue)
        {
            query = query.Where(b => b.Status == request.Status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var boards = await query
            .OrderBy(b => b.OrderIndex)
            .ThenByDescending(b => b.UpdatedAt)
            .Skip(paging.Skip)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<BoardSummaryDto>(
            boards.Select(BoardSummaryDto.From).ToList(),
            paging.NormalizedPage,
            paging.NormalizedPageSize,
            totalCount);
    }
}
