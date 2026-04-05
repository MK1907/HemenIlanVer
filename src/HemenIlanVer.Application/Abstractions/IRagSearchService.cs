using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Abstractions;

public interface IRagSearchService
{
    Task<PagedListingsDto> HybridSearchAsync(
        string query,
        Guid? categoryId,
        Guid? cityId,
        decimal? minPrice,
        decimal? maxPrice,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<ListingSummaryDto>> FindSimilarAsync(
        Guid listingId,
        int count = 6,
        CancellationToken ct = default);
}
