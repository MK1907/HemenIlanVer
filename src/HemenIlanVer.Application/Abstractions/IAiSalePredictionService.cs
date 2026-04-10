using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiSalePredictionService
{
    Task<SalePredictionDto> PredictAsync(Guid listingId, CancellationToken ct = default);
}
