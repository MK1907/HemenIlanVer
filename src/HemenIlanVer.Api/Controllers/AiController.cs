using HemenIlanVer.Application.Abstractions;
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
    private readonly IAiSearchExtractionService _searchAi;

    public AiController(IAiListingExtractionService listingAi, IAiSearchExtractionService searchAi)
    {
        _listingAi = listingAi;
        _searchAi = searchAi;
    }

    [HttpPost("detect-listing-category")]
    [Authorize]
    public async Task<ActionResult<ListingCategoryDetectResponse>> DetectListingCategory([FromBody] ListingCategoryDetectRequest request, CancellationToken ct)
    {
        var result = await _listingAi.DetectListingCategoryAsync(User.GetUserId(), request, ct);
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
