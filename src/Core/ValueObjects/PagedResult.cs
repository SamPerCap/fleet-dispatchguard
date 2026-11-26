namespace DispatchGuard.Core.ValueObjects;

/// <summary>Carries the total, so that the caller knows whether more pages exist without a second round-trip.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;

    private PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalCount"/> is negative, or <paramref name="page"/> or <paramref name="pageSize"/> are less than 1.
    /// </exception>
    public static PagedResult<T> Create(IEnumerable<T> items, int totalCount, int page, int pageSize)
    {
        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be negative.");
        }

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Page must be 1 or greater.");
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be 1 or greater.");
        }

        return new PagedResult<T>((items ?? Enumerable.Empty<T>()).ToList().AsReadOnly(), totalCount, page, pageSize);
    }

    public static PagedResult<T> Empty(int page, int pageSize) => Create([], 0, page, pageSize);
}
