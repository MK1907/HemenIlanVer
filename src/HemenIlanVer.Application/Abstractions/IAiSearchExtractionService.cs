using HemenIlanVer.Contracts.Ai;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiSearchExtractionService
{
    Task<SearchExtractResponse> ExtractAsync(Guid? userId, SearchExtractRequest request, CancellationToken cancellationToken = default);
}
