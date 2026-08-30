namespace Application.Common;

/// <summary>Envelope for paginated list endpoints (spec section 7).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNext => Page < TotalPages;

    public bool HasPrevious => Page > 1;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Common paging query parameters with sane, bounded defaults.</summary>
public sealed record PageQuery
{
    private const int MaxPageSize = 200;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => 50,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
