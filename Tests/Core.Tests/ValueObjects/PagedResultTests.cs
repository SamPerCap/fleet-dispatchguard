using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class PagedResultTests
{
    [Fact]
    public void TotalPages_TotalCountNotDivisibleByPageSize_RoundsUp()
    {
        var items = new[] { 1, 2, 3 };

        var result = PagedResult<int>.Create(items, totalCount: 10, page: 1, pageSize: 3);

        Assert.Equal(4, result.TotalPages);
    }

    [Fact]
    public void TotalPages_TotalCountExactlyDivisibleByPageSize_DoesNotRoundUp()
    {
        var items = new[] { 1, 2, 3, 4, 5 };

        var result = PagedResult<int>.Create(items, totalCount: 10, page: 1, pageSize: 5);

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void TotalPages_TotalCountZero_IsZero()
    {
        var result = PagedResult<int>.Create(Array.Empty<int>(), totalCount: 0, page: 1, pageSize: 10);

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void HasNextPage_OnFirstPageOfMultiplePages_IsTrue()
    {
        var result = PagedResult<int>.Create(new[] { 1, 2, 3 }, totalCount: 10, page: 1, pageSize: 3);

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_OnIntermediatePage_IsTrue()
    {
        var result = PagedResult<int>.Create(new[] { 4, 5, 6 }, totalCount: 10, page: 2, pageSize: 3);

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_OnLastPage_IsFalse()
    {
        var result = PagedResult<int>.Create(new[] { 10 }, totalCount: 10, page: 4, pageSize: 3);

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void HasPreviousPage_OnFirstPage_IsFalse()
    {
        var result = PagedResult<int>.Create(new[] { 1, 2, 3 }, totalCount: 10, page: 1, pageSize: 3);

        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_OnIntermediatePage_IsTrue()
    {
        var result = PagedResult<int>.Create(new[] { 4, 5, 6 }, totalCount: 10, page: 2, pageSize: 3);

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_OnLastPage_IsTrue()
    {
        var result = PagedResult<int>.Create(new[] { 10 }, totalCount: 10, page: 4, pageSize: 3);

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void Create_WithNegativeTotalCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PagedResult<int>.Create(Array.Empty<int>(), totalCount: -1, page: 1, pageSize: 10));
    }

    [Fact]
    public void Create_WithPageZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PagedResult<int>.Create(Array.Empty<int>(), totalCount: 0, page: 0, pageSize: 10));
    }

    [Fact]
    public void Create_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PagedResult<int>.Create(Array.Empty<int>(), totalCount: 0, page: -1, pageSize: 10));
    }

    [Fact]
    public void Create_WithPageSizeZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PagedResult<int>.Create(Array.Empty<int>(), totalCount: 0, page: 1, pageSize: 0));
    }

    [Fact]
    public void Create_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PagedResult<int>.Create(Array.Empty<int>(), totalCount: 0, page: 1, pageSize: -5));
    }

    [Fact]
    public void Create_WithValidArguments_PopulatesItemsAndMetadata()
    {
        var items = new[] { 1, 2, 3 };

        var result = PagedResult<int>.Create(items, totalCount: 3, page: 1, pageSize: 3);

        Assert.Equal(items, result.Items);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.PageSize);
    }

    [Fact]
    public void Empty_ProducesZeroTotalCount_AndEmptyItems()
    {
        var result = PagedResult<int>.Empty(page: 1, pageSize: 10);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Empty_ProducesZeroTotalPages()
    {
        var result = PagedResult<int>.Empty(page: 1, pageSize: 10);

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void Empty_HasNextPage_IsFalse()
    {
        var result = PagedResult<int>.Empty(page: 1, pageSize: 10);

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Empty_PreservesRequestedPageAndPageSize()
    {
        var result = PagedResult<int>.Empty(page: 2, pageSize: 25);

        Assert.Equal(2, result.Page);
        Assert.Equal(25, result.PageSize);
    }

    [Fact]
    public void Items_IsExposedAsReadOnlyListContract()
    {
        var source = new List<int> { 1, 2, 3 };

        var result = PagedResult<int>.Create(source, totalCount: 3, page: 1, pageSize: 3);

        // Callers cannot add, remove or replace elements without an explicit cast.
        Assert.IsAssignableFrom<IReadOnlyList<int>>(result.Items);
    }

    [Fact]
    public void Items_MutatingSourceCollectionAfterCreate_DoesNotAffectResult()
    {
        var source = new List<int> { 1, 2, 3 };
        var result = PagedResult<int>.Create(source, totalCount: 3, page: 1, pageSize: 3);

        source.Add(4);

        // A snapshot of the items, not an alias of the caller's list.
        Assert.Equal(3, result.Items.Count);
    }
}
