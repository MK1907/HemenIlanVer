using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Api.Extensions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Contracts.Listings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HemenIlanVer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ListingsController : ControllerBase
{
    private readonly IListingService _listings;
    private readonly IRagSearchService _rag;
    private readonly IListingIndexService _indexer;
    private readonly IAiSearchExtractionService _searchExtractor;
    private readonly IAiSalePredictionService _salePrediction;
    private readonly IAiPhotoAnalysisService _photoAnalysis;

    public ListingsController(
        IListingService listings,
        IRagSearchService rag,
        IListingIndexService indexer,
        IAiSearchExtractionService searchExtractor,
        IAiSalePredictionService salePrediction,
        IAiPhotoAnalysisService photoAnalysis)
    {
        _listings = listings;
        _rag = rag;
        _indexer = indexer;
        _searchExtractor = searchExtractor;
        _salePrediction = salePrediction;
        _photoAnalysis = photoAnalysis;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? cityId,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? q,
        [FromQuery] string? filterModel,
        [FromQuery] string? filterGear,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = null,
        [FromQuery] string? searchMode = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(q) && searchMode is null or "hybrid" or "vector")
        {
            try
            {
                Guid? userId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
                var extraction = await _searchExtractor.ExtractAsync(
                    userId, new SearchExtractRequest(q, categoryId), ct);

                var effectiveCat = extraction.CategoryId ?? categoryId;
                var effectiveCity = extraction.CityId ?? cityId;
                var effectiveMinPrice = extraction.MinPrice ?? minPrice;
                var effectiveMaxPrice = extraction.MaxPrice ?? maxPrice;
                var attrFilters = extraction.Filters.Count > 0 ? extraction.Filters : null;

                var result = await _rag.HybridSearchAsync(
                    q, effectiveCat, effectiveCity,
                    effectiveMinPrice, effectiveMaxPrice,
                    attrFilters, page, pageSize, ct);
                return Ok(result);
            }
            catch
            {
                // AI arama başarısız → normal aramaya düş
            }
        }

        var fallback = await _listings.SearchAsync(categoryId, cityId, minPrice, maxPrice, q, filterModel, filterGear, page, pageSize, sort, ct);
        return Ok(fallback);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var x = await _listings.GetByIdAsync(id, ct);
        return x is null ? NotFound() : Ok(x);
    }

    [HttpGet("{id:guid}/similar")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSimilar(Guid id, [FromQuery] int count = 6, CancellationToken ct = default)
    {
        var items = await _rag.FindSimilarAsync(id, count, ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateListingRequest request, CancellationToken ct)
    {
        var id = await _listings.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPost("reindex")]
    [Authorize]
    public async Task<IActionResult> ReindexAll(CancellationToken ct)
    {
        await _indexer.ReindexAllAsync(ct);
        return Ok(new { message = "Reindex completed" });
    }

    [HttpPost("{id:guid}/favorite")]
    [Authorize]
    public async Task<IActionResult> Favorite(Guid id, CancellationToken ct)
    {
        var added = await _listings.ToggleFavoriteAsync(User.GetUserId(), id, ct);
        return Ok(new { favorited = added });
    }

    [HttpGet("{id:guid}/sale-prediction")]
    [Authorize]
    public async Task<ActionResult<SalePredictionDto>> SalePrediction(Guid id, CancellationToken ct)
    {
        // Sadece ilanın sahibi görebilir
        var listing = await _listings.GetByIdAsync(id, ct);
        if (listing is null) return NotFound();
        if (listing.UserId != User.GetUserId()) return Forbid();

        var prediction = await _salePrediction.PredictAsync(id, ct);
        return Ok(prediction);
    }

    [HttpGet("{id:guid}/photo-analysis")]
    [Authorize]
    public async Task<ActionResult<PhotoAnalysisDto>> PhotoAnalysis(Guid id, CancellationToken ct)
    {
        var listing = await _listings.GetByIdAsync(id, ct);
        if (listing is null) return NotFound();
        var result = await _photoAnalysis.AnalyzeAsync(id, ct);
        return Ok(result);
    }
}
