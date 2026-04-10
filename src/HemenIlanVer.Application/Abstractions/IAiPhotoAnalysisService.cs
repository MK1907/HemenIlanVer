using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiPhotoAnalysisService
{
    Task<PhotoAnalysisDto> AnalyzeAsync(Guid listingId, CancellationToken ct = default);

    /// <summary>
    /// İlan oluşturma sırasında yüklenen fotoğraflardan özellik değerlerini tespit eder.
    /// </summary>
    Task<Dictionary<string, string>> ExtractAttributesFromPhotosAsync(
        IReadOnlyList<string> imageUrls, string categorySlug, CancellationToken ct = default);
}
