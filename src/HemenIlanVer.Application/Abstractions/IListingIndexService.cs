namespace HemenIlanVer.Application.Abstractions;

public interface IListingIndexService
{
    Task IndexListingAsync(Guid listingId, CancellationToken ct = default);
    Task ReindexAllAsync(CancellationToken ct = default);
}
