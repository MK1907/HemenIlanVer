using HemenIlanVer.Contracts.Ai;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiListingExtractionService
{
    Task<ListingCategoryDetectResponse> DetectListingCategoryAsync(Guid? userId, ListingCategoryDetectRequest request, CancellationToken cancellationToken = default);
}
