using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Application.Exceptions;
using HemenIlanVer.Api.Extensions;
using HemenIlanVer.Contracts.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HemenIlanVer.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiController : ControllerBase
{
    private readonly IAiListingExtractionService _listingAi;
    private readonly IAiListingPartialSuggestionService _partialSuggest;
    private readonly IAiSearchExtractionService _searchAi;

    public AiController(
        IAiListingExtractionService listingAi,
        IAiListingPartialSuggestionService partialSuggest,
        IAiSearchExtractionService searchAi)
    {
        _listingAi = listingAi;
        _partialSuggest = partialSuggest;
        _searchAi = searchAi;
    }

    [HttpPost("detect-listing-category")]
    [Authorize]
    public async Task<ActionResult<ListingCategoryDetectResponse>> DetectListingCategory([FromBody] ListingCategoryDetectRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _listingAi.DetectListingCategoryAsync(User.GetUserId(), request, ct);
            return Ok(result);
        }
        catch (InvalidListingPromptException ex)
        {
            return UnprocessableEntity(new { error = ex.Reason });
        }
    }

    /// <summary>Yazarken kısmi metne göre olası ilan yönleri (Türkçe kısa öneriler).</summary>
    [HttpPost("suggest-partial-listing")]
    [Authorize]
    public async Task<ActionResult<ListingPartialSuggestResponse>> SuggestPartialListing([FromBody] ListingPartialSuggestRequest request, CancellationToken ct)
    {
        var result = await _partialSuggest.SuggestAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("search-extract")]
    [AllowAnonymous]
    public async Task<ActionResult<SearchExtractResponse>> SearchExtract([FromBody] SearchExtractRequest request, CancellationToken ct)
    {
        var result = await _searchAi.ExtractAsync(null, request, ct);
        return Ok(result);
    }
}
