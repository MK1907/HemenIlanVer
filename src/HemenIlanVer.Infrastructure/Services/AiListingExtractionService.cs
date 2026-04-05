using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AiListingExtractionService : IAiListingExtractionService
{
    private readonly AppDbContext _db;
    private readonly IAiCategoryBootstrapService _categoryBootstrap;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _openAi;
    private readonly ILogger<AiListingExtractionService> _logger;

    public AiListingExtractionService(
        AppDbContext db,
        IAiCategoryBootstrapService categoryBootstrap,
        IHttpClientFactory httpFactory,
        IOptions<OpenAiOptions> openAi,
        ILogger<AiListingExtractionService> logger)
    {
        _db = db;
        _categoryBootstrap = categoryBootstrap;
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
        _logger = logger;
    }

    public async Task<ListingCategoryDetectResponse> DetectListingCategoryAsync(Guid? userId, ListingCategoryDetectRequest request, CancellationToken cancellationToken = default)
    {
        OpenAiGuard.RequireApiKey(_openAi.ApiKey, "Kategori ve alt kategori tespiti");

        var traceId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();

        ListingCategoryDetectResponse result;
        try
        {
            result = await CallOpenAiDetectAsync(request.Prompt, traceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI kategori tespiti başarısız.");
            throw;
        }

        sw.Stop();
        _db.AiExtractionLogs.Add(new AiExtractionLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = "listing_detect",
            Model = _openAi.Model,
            PromptLength = request.Prompt?.Length,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Success = true,
            OutputSummary = $"root={result.RootName};leaf={result.SuggestedLeafCategoryId}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return result with { TraceId = traceId, UsedMockProvider = false };
    }

    private async Task<ListingCategoryDetectResponse> CallOpenAiDetectAsync(string? prompt, Guid traceId, CancellationToken ct)
    {
        OpenAiGuard.RequireApiKey(_openAi.ApiKey, "Kategori ve alt kategori tespiti");

        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var roots = await _db.Categories.AsNoTracking()
            .Where(x => x.ParentId == null && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Name, x.Slug })
            .ToListAsync(ct);

        var children = await _db.Categories.AsNoTracking()
            .Where(x => x.ParentId != null && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.ParentId, x.Name, x.Slug })
            .ToListAsync(ct);

        var system =
            "Sen Türkiye ilan siteleri için ana kategori ve alt kategori seçeneklerini eşleştiren yardımcısın. SADECE geçerli JSON üret.\n" +
            "Mevcut ana kategoriler (slug): " + JsonSerializer.Serialize(roots) + "\n" +
            "Mevcut alt kategoriler (parentId, slug): " + JsonSerializer.Serialize(children) + "\n" +
            "Çıktı: rootSlug, suggestedChildSlug (null olabilir), confidence (0-1).\n" +
            "bootstrap: Kullanıcı niyeti yukarıdaki listede yoksa veya tam karşılığı yoksa doldur: " +
            "needed (bool), rootName, rootSlug (küçük harf tire), childName, childSlug, " +
            "filters (dizi: key, displayName, dataType String|Int|Decimal|Bool|Enum, required, options: valueKey+label dizisi). " +
            "Enum için options en az 2; diğer tiplerde options boş.\n" +
            "Önce mevcut slug seç; uygunsa bootstrap.needed=false. Yeni dikey için bootstrap ile 3-8 anlamlı filtre üret.";

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = prompt ?? "" }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, json);
        var root = JsonDocument.Parse(json).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("Boş içerik");

        var doc = JsonDocument.Parse(content).RootElement;
        await _categoryBootstrap.ApplyFromDetectDocumentAsync(doc, ct);

        roots = await _db.Categories.AsNoTracking()
            .Where(x => x.ParentId == null && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Name, x.Slug })
            .ToListAsync(ct);

        children = await _db.Categories.AsNoTracking()
            .Where(x => x.ParentId != null && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.ParentId, x.Name, x.Slug })
            .ToListAsync(ct);

        var rootSlug = doc.TryGetProperty("rootSlug", out var rs) ? rs.GetString() : null;
        var childSlug = doc.TryGetProperty("suggestedChildSlug", out var cs) ? cs.GetString() : null;
        var conf = doc.TryGetProperty("confidence", out var cf) && cf.TryGetDouble(out var cfd) ? cfd : 0.75;

        if (string.IsNullOrWhiteSpace(rootSlug))
            throw new InvalidOperationException("OpenAI yanıtında rootSlug yok veya geçersiz.");

        var rootCat = roots.FirstOrDefault(r => r.Slug == rootSlug);
        if (rootCat is null)
            throw new InvalidOperationException(
                $"OpenAI kategori yanıtı veritabanıyla eşleşmedi (rootSlug: {rootSlug}).");

        var subs = children.Where(c => c.ParentId == rootCat.Id)
            .Select(c => new SubCategoryOptionDto(c.Id, c.Name, c.Slug))
            .ToList();

        Guid? leafId = null;
        if (!string.IsNullOrEmpty(childSlug))
        {
            var match = children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == childSlug);
            if (match is not null) leafId = match.Id;
        }

        string? sugTitle = null;
        string? sugDesc = null;
        decimal? sugPrice = null;
        IReadOnlyDictionary<string, string>? sugAttrs = null;

        if (leafId is not null)
        {
            try
            {
                (sugTitle, sugDesc, sugPrice, sugAttrs) =
                    await ExtractAttributeValuesAsync(client, prompt, leafId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI attribute değer çıkarma başarısız; kategori tespiti yine döner.");
            }
        }

        return new ListingCategoryDetectResponse(
            traceId,
            rootCat.Id,
            rootCat.Name,
            subs,
            leafId,
            conf,
            false,
            sugTitle,
            sugDesc,
            sugPrice,
            sugAttrs);
    }

    private async Task<(string? Title, string? Description, decimal? Price, IReadOnlyDictionary<string, string>? AttrValues)>
        ExtractAttributeValuesAsync(HttpClient client, string? prompt, Guid leafCategoryId, CancellationToken ct)
    {
        var attrs = await _db.CategoryAttributes.AsNoTracking()
            .Include(x => x.Options)
            .Where(x => x.CategoryId == leafCategoryId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        if (attrs.Count == 0)
            return (null, null, null, null);

        var attrSpec = attrs.Select(a => new
        {
            key = a.AttributeKey,
            displayName = a.DisplayName,
            dataType = a.DataType.ToString(),
            required = a.IsRequired,
            options = a.Options.OrderBy(o => o.SortOrder).Select(o => new { o.ValueKey, o.Label }).ToList()
        }).ToList();

        var system =
            "Sen ilan metni ayrıştırıcısın. Kullanıcının yazdığı metne göre aşağıdaki alan değerlerini çıkar.\n" +
            "Alanlar: " + JsonSerializer.Serialize(attrSpec) + "\n" +
            "Kurallar:\n" +
            "- Enum tipli alanlar için MUTLAKA options içindeki valueKey değerlerinden birini seç.\n" +
            "- Int / Decimal / Money alanları sayı olarak ver.\n" +
            "- Bool alanları true/false olarak ver.\n" +
            "- Metinden çıkaramadığın alanları atla (JSON'a koyma).\n" +
            "- Ayrıca şunları çıkar: suggestedTitle (kısa ilan başlığı), suggestedDescription (2-3 cümle açıklama), suggestedPrice (TRY cinsinden, null olabilir).\n" +
            "SADECE geçerli JSON: {\"suggestedTitle\":\"...\",\"suggestedDescription\":\"...\",\"suggestedPrice\":null,\"attributes\":{\"key\":\"value\",...}}";

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = prompt ?? "" }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, raw);

        var root = JsonDocument.Parse(raw).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var doc = JsonDocument.Parse(content).RootElement;

        var title = doc.TryGetProperty("suggestedTitle", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
        var desc = doc.TryGetProperty("suggestedDescription", out var sd) && sd.ValueKind == JsonValueKind.String ? sd.GetString() : null;
        decimal? price = null;
        if (doc.TryGetProperty("suggestedPrice", out var sp))
        {
            if (sp.ValueKind == JsonValueKind.Number && sp.TryGetDecimal(out var d)) price = d;
        }

        var values = new Dictionary<string, string>();
        if (doc.TryGetProperty("attributes", out var attrObj) && attrObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrObj.EnumerateObject())
            {
                var val = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
                if (val is not null)
                    values[prop.Name] = val;
            }
        }

        return (title, desc, price, values.Count > 0 ? values : null);
    }
}
