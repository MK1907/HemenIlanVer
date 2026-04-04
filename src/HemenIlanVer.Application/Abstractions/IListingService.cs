using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Abstractions;

public interface IListingService
{
    Task<PagedListingsDto> SearchAsync(
        Guid? categoryId,
        Guid? cityId,
        decimal? minPrice,
        decimal? maxPrice,
        string? q,
        string? filterModel,
        string? filterGear,
        int page,
        int pageSize,
        string? sort,
        CancellationToken cancellationToken = default);
    Task<ListingDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid userId, CreateListingRequest request, CancellationToken cancellationToken = default);
    Task<bool> ToggleFavoriteAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default);
}
