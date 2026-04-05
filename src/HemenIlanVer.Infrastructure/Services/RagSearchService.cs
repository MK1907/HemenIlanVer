using System.Globalization;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Listings;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class RagSearchService : IRagSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<RagSearchService> _log;
    private const int RrfK = 60;
    private const int CandidatePoolSize = 50;

    public RagSearchService(AppDbContext db, IEmbeddingService embedding, ILogger<RagSearchService> log)
    {
        _db = db;
        _embedding = embedding;
        _log = log;
    }

    public async Task<PagedListingsDto> HybridSearchAsync(
        string query,
        Guid? categoryId,
        Guid? cityId,
        decimal? minPrice,
        decimal? maxPrice,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var vectorTask = GetVectorRankedIdsAsync(query, categoryId, cityId, minPrice, maxPrice, ct);
        var keywordTask = GetKeywordRankedIdsAsync(query, categoryId, cityId, minPrice, maxPrice, ct);

        await Task.WhenAll(vectorTask, keywordTask);

        var vectorResults = vectorTask.Result;
        var keywordResults = keywordTask.Result;

        var fused = FuseRRF(vectorResults, keywordResults);
        var total = fused.Count;
        var pageIds = fused.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();

        if (pageIds.Count == 0)
            return new PagedListingsDto([], page, pageSize, total);

        var listings = await _db.Listings.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.City)
            .Include(x => x.District)
            .Include(x => x.Images)
            .Where(x => pageIds.Contains(x.Id))
            .ToListAsync(ct);

        var ordered = pageIds
            .Select(id => listings.FirstOrDefault(l => l.Id == id))
            .Where(l => l is not null)
            .Select(x => new ListingSummaryDto(
                x!.Id, x.Title, x.Price, x.Currency,
                x.City?.Name ?? "", x.District?.Name,
                x.Category.Name, x.CreatedAt,
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                x.ViewCount))
            .ToList();

        return new PagedListingsDto(ordered, page, pageSize, total);
    }

    public async Task<IReadOnlyList<ListingSummaryDto>> FindSimilarAsync(Guid listingId, int count = 6, CancellationToken ct = default)
    {
        var embeddingRow = await _db.ListingEmbeddings.FirstOrDefaultAsync(e => e.ListingId == listingId, ct);
        if (embeddingRow is null)
            return [];

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = (NpgsqlCommand)conn.CreateCommand();
        cmd.CommandText = """
            SELECT le."ListingId", 1 - (le."Embedding" <=> ref."Embedding") AS similarity
            FROM listing_embeddings le
            CROSS JOIN (SELECT "Embedding" FROM listing_embeddings WHERE "ListingId" = @refId) ref
            JOIN listings l ON l."Id" = le."ListingId"
            WHERE le."ListingId" != @refId
              AND l."Status" = @published
              AND le."Embedding" IS NOT NULL
            ORDER BY le."Embedding" <=> ref."Embedding"
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("refId", listingId);
        cmd.Parameters.AddWithValue("published", (int)ListingStatus.Published);
        cmd.Parameters.AddWithValue("limit", count);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));

        if (ids.Count == 0) return [];

        var listings = await _db.Listings.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.City)
            .Include(x => x.District)
            .Include(x => x.Images)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);

        return ids
            .Select(id => listings.FirstOrDefault(l => l.Id == id))
            .Where(l => l is not null)
            .Select(x => new ListingSummaryDto(
                x!.Id, x.Title, x.Price, x.Currency,
                x.City?.Name ?? "", x.District?.Name,
                x.Category.Name, x.CreatedAt,
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                x.ViewCount))
            .ToList();
    }

    private async Task<List<(Guid Id, int Rank)>> GetVectorRankedIdsAsync(
        string query, Guid? categoryId, Guid? cityId, decimal? minPrice, decimal? maxPrice, CancellationToken ct)
    {
        try
        {
            var queryVector = await _embedding.GenerateEmbeddingAsync(query, ct);
            if (queryVector.Length == 0) return [];

            var vectorStr = "[" + string.Join(",", queryVector.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            var filterClauses = BuildFilterClauses(categoryId, cityId, minPrice, maxPrice);

            await using var cmd = (NpgsqlCommand)conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT le."ListingId"
                FROM listing_embeddings le
                JOIN listings l ON l."Id" = le."ListingId"
                WHERE l."Status" = @published
                  AND le."Embedding" IS NOT NULL
                  {filterClauses.Sql}
                ORDER BY le."Embedding" <=> @qvec::vector
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("published", (int)ListingStatus.Published);
            cmd.Parameters.AddWithValue("qvec", vectorStr);
            cmd.Parameters.AddWithValue("limit", CandidatePoolSize);
            foreach (var p in filterClauses.Params)
                cmd.Parameters.Add(p);

            var results = new List<(Guid Id, int Rank)>();
            int rank = 1;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add((reader.GetGuid(0), rank++));

            return results;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vector search failed, falling back to keyword-only");
            return [];
        }
    }

    private async Task<List<(Guid Id, int Rank)>> GetKeywordRankedIdsAsync(
        string query, Guid? categoryId, Guid? cityId, decimal? minPrice, decimal? maxPrice, CancellationToken ct)
    {
        var tokens = query.Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeLikeToken)
            .Where(t => t.Length >= 1)
            .ToList();

        var q = _db.Listings.AsNoTracking()
            .Where(x => x.Status == ListingStatus.Published);

        if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId.Value);
        if (cityId.HasValue) q = q.Where(x => x.CityId == cityId.Value);
        if (minPrice.HasValue) q = q.Where(x => x.Price >= minPrice.Value);
        if (maxPrice.HasValue) q = q.Where(x => x.Price <= maxPrice.Value);

        foreach (var token in tokens)
        {
            var t = token;
            q = q.Where(x =>
                EF.Functions.ILike(x.Title, $"%{t}%") ||
                EF.Functions.ILike(x.Description, $"%{t}%"));
        }

        var ids = await q
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Take(CandidatePoolSize)
            .Select(x => x.Id)
            .ToListAsync(ct);

        return ids.Select((id, i) => (id, Rank: i + 1)).ToList();
    }

    private static List<(Guid Id, double Score)> FuseRRF(
        List<(Guid Id, int Rank)> vectorResults,
        List<(Guid Id, int Rank)> keywordResults)
    {
        var scores = new Dictionary<Guid, double>();

        foreach (var (id, rank) in vectorResults)
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (RrfK + rank);

        foreach (var (id, rank) in keywordResults)
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (RrfK + rank);

        return scores
            .OrderByDescending(x => x.Value)
            .Select(x => (x.Key, x.Value))
            .ToList();
    }

    private static (string Sql, List<NpgsqlParameter> Params) BuildFilterClauses(
        Guid? categoryId, Guid? cityId, decimal? minPrice, decimal? maxPrice)
    {
        var parts = new List<string>();
        var parms = new List<NpgsqlParameter>();

        if (categoryId.HasValue)
        {
            parts.Add("""AND l."CategoryId" = @catId""");
            parms.Add(new NpgsqlParameter("catId", categoryId.Value));
        }
        if (cityId.HasValue)
        {
            parts.Add("""AND l."CityId" = @cityId""");
            parms.Add(new NpgsqlParameter("cityId", cityId.Value));
        }
        if (minPrice.HasValue)
        {
            parts.Add("""AND l."Price" >= @minP""");
            parms.Add(new NpgsqlParameter("minP", minPrice.Value));
        }
        if (maxPrice.HasValue)
        {
            parts.Add("""AND l."Price" <= @maxP""");
            parms.Add(new NpgsqlParameter("maxP", maxPrice.Value));
        }

        return (string.Join("\n", parts), parms);
    }

    private static string SanitizeLikeToken(string s) =>
        new(s.Where(c => c is not '%' and not '_').ToArray());
}
