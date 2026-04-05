using HemenIlanVer.Contracts.Ai;

namespace HemenIlanVer.Application.Abstractions;

public interface IAiListingPartialSuggestionService
{
    Task<ListingPartialSuggestResponse> SuggestAsync(ListingPartialSuggestRequest request, CancellationToken cancellationToken = default);
}
