using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AiSearchExtractionService : IAiSearchExtractionService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _openAi;

    public AiSearchExtractionService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IOptions<OpenAiOptions> openAi)
    {
        _db = db;
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
    }

    public async Task<SearchExtractResponse> ExtractAsync(Guid? userId, SearchExtractRequest request, CancellationToken cancellationToken = default)
    {
        OpenAiGuard.RequireApiKey(_openAi.ApiKey, "Akıllı arama (search-extract)");

        var traceId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();

        var result = await CallOpenAiAsync(request, cancellationToken);

        sw.Stop();

        Guid? cityId = null;
        string? cityName = result.CityName;
        if (!string.IsNullOrWhiteSpace(cityName))
        {
            var norm = cityName.Trim();
            cityId = await _db.Cities.AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Name, norm))
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        _db.SearchLogs.Add(new SearchLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RawQuery = request.Prompt,
            ExtractedJson = JsonSerializer.Serialize(result),
            ResultCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.AiExtractionLogs.Add(new AiExtractionLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = "search",
            Model = _openAi.Model,
            PromptLength = request.Prompt?.Length,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Success = true,
            OutputSummary = $"city={cityName}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return result with { TraceId = traceId, CityId = cityId ?? result.CityId, UsedMockProvider = false };
    }

    private async Task<SearchExtractResponse> CallOpenAiAsync(SearchExtractRequest request, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var cats = await _db.Categories.AsNoTracking()
            .Where(x => x.ParentId != null)
            .Select(x => new { x.Id, x.Name, x.Slug })
            .Take(40)
            .ToListAsync(ct);

        var system = $"""
            Sen ilan arama filtresi çıkaran yardımcısın. SADECE JSON üret.
            Kategoriler: {JsonSerializer.Serialize(cats)}
            Çıktı: intent (search), categoryId (uuid|null), filters (object: key->string), cityName, minPrice, maxPrice, sortPreference (price_asc|price_desc|km_asc|date_desc|null), confidence (0-1)
            filters anahtarları: model (ör. Egea, Corolla), gear (Manuel|Otomatik), brand, fuel vb. Metin araması gerekmeyen yapılandırılmış alanları buraya koy.
            Kurallar: "1 milyon altı" / "bir milyon altı" -> maxPrice 1000000. Şehir adını cityName olarak ver (ör. İstanbul). Düşük km isteği -> sortPreference km_asc.
            """;

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = request.Prompt }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, json);
        var root = JsonDocument.Parse(json).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("Boş içerik");

        var doc = JsonDocument.Parse(content).RootElement;
        var filters = new Dictionary<string, string?>();
        if (doc.TryGetProperty("filters", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in f.EnumerateObject())
                filters[p.Name] = p.Value.GetString();
        }

        Guid? catId = null;
        if (doc.TryGetProperty("categoryId", out var cid) && cid.ValueKind == JsonValueKind.String
            && Guid.TryParse(cid.GetString(), out var gid))
            catId = gid;

        decimal? min = null, max = null;
        if (doc.TryGetProperty("minPrice", out var minP) && minP.TryGetDecimal(out var minD)) min = minD;
        if (doc.TryGetProperty("maxPrice", out var maxP) && maxP.TryGetDecimal(out var maxD)) max = maxD;

        var city = doc.TryGetProperty("cityName", out var cn) ? cn.GetString() : null;
        var sort = doc.TryGetProperty("sortPreference", out var sp) ? sp.GetString() : null;
        var conf = doc.TryGetProperty("confidence", out var cf) && cf.TryGetDouble(out var cfd) ? cfd : 0.7;

        return new SearchExtractResponse(
            Guid.Empty,
            catId,
            filters,
            null,
            city,
            min,
            max,
            sort,
            conf,
            false);
    }
}
