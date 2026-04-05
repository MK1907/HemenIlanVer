using System.Globalization;
using System.Text;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class ListingIndexService : IListingIndexService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<ListingIndexService> _log;

    public ListingIndexService(AppDbContext db, IEmbeddingService embedding, ILogger<ListingIndexService> log)
    {
        _db = db;
        _embedding = embedding;
        _log = log;
    }

    public async Task IndexListingAsync(Guid listingId, CancellationToken ct = default)
    {
        var listing = await _db.Listings
            .Include(l => l.Category).ThenInclude(c => c.Parent)
            .Include(l => l.City)
            .Include(l => l.District)
            .Include(l => l.AttributeValues).ThenInclude(v => v.CategoryAttribute)
            .FirstOrDefaultAsync(l => l.Id == listingId, ct);

        if (listing is null)
        {
            _log.LogWarning("IndexListingAsync: Listing {Id} not found", listingId);
            return;
        }

        var searchableText = BuildSearchableText(listing);
        var vector = await _embedding.GenerateEmbeddingAsync(searchableText, ct);
        if (vector.Length == 0)
        {
            _log.LogWarning("IndexListingAsync: Empty embedding for listing {Id}", listingId);
            return;
        }

        var existing = await _db.ListingEmbeddings.FirstOrDefaultAsync(e => e.ListingId == listingId, ct);
        if (existing is not null)
        {
            existing.SearchableText = searchableText;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await UpdateEmbeddingVectorAsync(existing.Id, vector, ct);
        }
        else
        {
            var entry = new ListingEmbedding
            {
                Id = Guid.NewGuid(),
                ListingId = listingId,
                SearchableText = searchableText,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ListingEmbeddings.Add(entry);
            await _db.SaveChangesAsync(ct);

            await UpdateEmbeddingVectorAsync(entry.Id, vector, ct);
        }

        _log.LogInformation("Indexed listing {Id} ({Dims} dims)", listingId, vector.Length);
    }

    public async Task ReindexAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.Listings
            .Where(l => l.Status == ListingStatus.Published)
            .Select(l => l.Id)
            .ToListAsync(ct);

        _log.LogInformation("Reindexing {Count} published listings", ids.Count);

        foreach (var id in ids)
        {
            try
            {
                await IndexListingAsync(id, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to index listing {Id}", id);
            }
        }
    }

    private async Task UpdateEmbeddingVectorAsync(Guid embeddingId, float[] vector, CancellationToken ct)
    {
        var vectorStr = "[" + string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";
        await _db.Database.ExecuteSqlRawAsync(
            """UPDATE listing_embeddings SET "Embedding" = @p0::vector WHERE "Id" = @p1""",
            new object[] { vectorStr, embeddingId },
            ct);
    }

    private static string BuildSearchableText(Listing listing)
    {
        var sb = new StringBuilder();

        if (listing.Category?.Parent is not null)
            sb.Append(listing.Category.Parent.Name).Append(" > ");
        if (listing.Category is not null)
            sb.Append(listing.Category.Name);

        sb.Append(" | ").Append(listing.Title);
        sb.Append(" | ").Append(listing.Description);

        if (listing.Price.HasValue)
            sb.Append(" | Fiyat: ").Append(listing.Price.Value.ToString("N0", CultureInfo.InvariantCulture)).Append(' ').Append(listing.Currency);

        if (listing.City is not null)
            sb.Append(" | Şehir: ").Append(listing.City.Name);

        foreach (var av in listing.AttributeValues)
        {
            var key = av.CategoryAttribute?.DisplayName ?? av.CategoryAttribute?.AttributeKey ?? "attr";
            var val = av.ValueText
                ?? av.ValueInt?.ToString()
                ?? av.ValueDecimal?.ToString(CultureInfo.InvariantCulture)
                ?? (av.ValueBool.HasValue ? (av.ValueBool.Value ? "Evet" : "Hayır") : null);
            if (!string.IsNullOrWhiteSpace(val))
                sb.Append(" | ").Append(key).Append(": ").Append(val);
        }

        return sb.ToString();
    }
}
