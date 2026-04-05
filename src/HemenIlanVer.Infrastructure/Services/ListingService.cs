using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Listings;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class ListingService : IListingService
{
    private readonly AppDbContext _db;

    public ListingService(AppDbContext db) => _db = db;

    public async Task<PagedListingsDto> SearchAsync(
        Guid? categoryId,
        Guid? cityId,
        decimal? minPrice,
        decimal? maxPrice,
        string? q,
        string? filterModel,
        string? filterGear,
        int page,
        int pageSize,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Listings.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.City)
            .Include(x => x.District)
            .Include(x => x.Images)
            .Where(x => x.Status == ListingStatus.Published);

        if (categoryId.HasValue)
            query = query.Where(x => x.CategoryId == categoryId.Value);
        if (cityId.HasValue)
            query = query.Where(x => x.CityId == cityId.Value);
        if (minPrice.HasValue)
            query = query.Where(x => x.Price >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(x => x.Price <= maxPrice.Value);

        if (!string.IsNullOrWhiteSpace(filterModel))
        {
            var m = SanitizeLikeToken(filterModel.Trim());
            if (m.Length > 0)
                query = query.Where(l =>
                    _db.ListingAttributeValues.Any(lav =>
                        lav.ListingId == l.Id &&
                        _db.CategoryAttributes.Any(ca =>
                            ca.Id == lav.CategoryAttributeId &&
                            ca.AttributeKey == "model" &&
                            lav.ValueText != null &&
                            EF.Functions.ILike(lav.ValueText, $"%{m}%"))));
        }

        if (!string.IsNullOrWhiteSpace(filterGear))
        {
            var g = SanitizeLikeToken(filterGear.Trim());
            if (g.Length > 0)
                query = query.Where(l =>
                    _db.ListingAttributeValues.Any(lav =>
                        lav.ListingId == l.Id &&
                        _db.CategoryAttributes.Any(ca =>
                            ca.Id == lav.CategoryAttributeId &&
                            ca.AttributeKey == "gear" &&
                            lav.ValueText != null &&
                            EF.Functions.ILike(lav.ValueText, $"%{g}%"))));
        }

        foreach (var token in TokenizeSearchQuery(q))
        {
            var t = token;
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, $"%{t}%") ||
                EF.Functions.ILike(x.Description, $"%{t}%"));
        }

        query = sort switch
        {
            "price_asc" => query.OrderBy(x => x.Price),
            "price_desc" => query.OrderByDescending(x => x.Price),
            "date_desc" => query.OrderByDescending(x => x.PublishedAt ?? x.CreatedAt),
            "km_asc" => query.OrderBy(l =>
                _db.ListingAttributeValues
                    .Where(v => v.ListingId == l.Id)
                    .Join(_db.CategoryAttributes, v => v.CategoryAttributeId, ca => ca.Id, (v, ca) => new { v, ca })
                    .Where(x => x.ca.AttributeKey == "km")
                    .Select(x => x.v.ValueInt ?? int.MaxValue)
                    .FirstOrDefault()),
            _ => query.OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var summaries = items.Select(x => new ListingSummaryDto(
            x.Id,
            x.Title,
            x.Price,
            x.Currency,
            x.City?.Name ?? "",
            x.District?.Name,
            x.Category.Name,
            x.CreatedAt,
            x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
            x.ViewCount
        )).ToList();

        return new PagedListingsDto(summaries, page, pageSize, total);
    }

    public async Task<ListingDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var x = await _db.Listings
            .Include(l => l.Category)
            .Include(l => l.City)
            .Include(l => l.District)
            .Include(l => l.AttributeValues).ThenInclude(v => v.CategoryAttribute)
            .Include(l => l.Images)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (x is null) return null;

        if (x.Status == ListingStatus.Published)
        {
            await _db.Listings.Where(l => l.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.ViewCount, l => l.ViewCount + 1), cancellationToken);
            x = await _db.Listings.AsNoTracking()
                .Include(l => l.Category)
                .Include(l => l.City)
                .Include(l => l.District)
                .Include(l => l.AttributeValues).ThenInclude(v => v.CategoryAttribute)
                .Include(l => l.Images)
                .FirstAsync(l => l.Id == id, cancellationToken);
        }

        var attr = x.AttributeValues.ToDictionary(
            v => v.CategoryAttribute.AttributeKey,
            v => v.ValueText ?? v.ValueInt?.ToString() ?? v.ValueDecimal?.ToString() ?? v.ValueBool?.ToString() ?? "");

        return new ListingDetailDto(
            x.Id,
            x.UserId,
            x.Title,
            x.Description,
            x.Price,
            x.Currency,
            x.ListingType.ToString(),
            x.Status.ToString(),
            x.CategoryId,
            x.Category.Name,
            x.CityId,
            x.City?.Name,
            x.DistrictId,
            x.District?.Name,
            attr,
            x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
            x.CreatedAt,
            x.ViewCount);
    }

    public async Task<Guid> CreateAsync(Guid userId, CreateListingRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ListingType>(request.ListingType, true, out var lt))
            lt = ListingType.Satilik;

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CategoryId = request.CategoryId,
            Title = request.Title,
            Description = request.Description,
            Price = request.Price,
            Currency = request.Currency,
            ListingType = lt,
            Status = request.Publish ? ListingStatus.Published : ListingStatus.Draft,
            CityId = request.CityId,
            DistrictId = request.DistrictId,
            PublishedAt = request.Publish ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Listings.Add(listing);

        foreach (var a in request.Attributes)
        {
            _db.ListingAttributeValues.Add(new ListingAttributeValue
            {
                Id = Guid.NewGuid(),
                ListingId = listing.Id,
                CategoryAttributeId = a.CategoryAttributeId,
                ValueText = a.ValueText,
                ValueInt = a.ValueInt,
                ValueDecimal = a.ValueDecimal,
                ValueBool = a.ValueBool,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return listing.Id;
    }

    public async Task<bool> ToggleFavoriteAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ListingId == listingId, cancellationToken);
        if (existing is not null)
        {
            _db.Favorites.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
            return false;
        }

        _db.Favorites.Add(new Favorite
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ListingId = listingId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IEnumerable<string> TokenizeSearchQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) yield break;
        foreach (var part in q.Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = SanitizeLikeToken(part);
            if (t.Length >= 1)
                yield return t;
        }
    }

    private static string SanitizeLikeToken(string s)
    {
        var chars = s.Where(c => c is not '%' and not '_').ToArray();
        return new string(chars).Trim();
    }
}
