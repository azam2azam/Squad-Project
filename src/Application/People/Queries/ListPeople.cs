using Application.Abstractions;
using Application.Common;
using Application.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.People.Queries;

/// <summary>
/// Roster listing, and the typeahead behind "pick a person, do not retype them".
/// Inactive people are excluded by default so the picker only offers current staff;
/// the roster manager passes IncludeInactive so they can be reactivated.
/// </summary>
public sealed record ListPeopleQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<PersonDto>>;

public sealed class ListPeopleQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPeopleQuery, PagedResult<PersonDto>>
{
    public async Task<PagedResult<PersonDto>> Handle(
        ListPeopleQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageQuery { Page = request.Page, PageSize = request.PageSize };

        var query = db.People.AsQueryable();

        if (!request.IncludeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.FullName, $"%{term}%") ||
                (p.Email != null && EF.Functions.Like(p.Email, $"%{term}%")) ||
                (p.DefaultDetail != null && EF.Functions.Like(p.DefaultDetail, $"%{term}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var people = await query
            // Active first, then alphabetical: the picker should never lead with leavers.
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.FullName)
            .Skip(paging.Skip)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PersonDto>(
            people.Select(PersonDto.From).ToList(),
            paging.NormalizedPage,
            paging.NormalizedPageSize,
            totalCount);
    }
}
