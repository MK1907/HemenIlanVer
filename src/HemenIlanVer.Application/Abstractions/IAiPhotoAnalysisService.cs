using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiPhotoAnalysisService
{
    Task<PhotoAnalysisDto> AnalyzeAsync(Guid listingId, CancellationToken ct = default);
}
