using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Listings;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AiPhotoAnalysisService : IAiPhotoAnalysisService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _openAi;
    private readonly ILogger<AiPhotoAnalysisService> _logger;

    public AiPhotoAnalysisService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IOptions<OpenAiOptions> openAi,
        ILogger<AiPhotoAnalysisService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
        _logger = logger;
    }

    public async Task<PhotoAnalysisDto> AnalyzeAsync(Guid listingId, CancellationToken ct = default)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == listingId, ct)
            ?? throw new InvalidOperationException("İlan bulunamadı.");

        var imageUrls = listing.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Url)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Take(5)
            .ToList();

        if (imageUrls.Count == 0)
        {
            return new PhotoAnalysisDto(
                OverallConditionScore: 0,
                ConditionLabel: "Bilinmiyor",
                HasScratchOrDent: false,
                HasPaintDifference: false,
                SuspectedTaxiOrRental: false,
                InteriorDamage: false,
                Findings: [],
                Warnings: ["Bu ilan için fotoğraf bulunmuyor. Analiz yapılamadı."],
                Summary: "Fotoğraf eklenmemiş, görsel analiz mümkün değil."
            );
        }

        if (!string.IsNullOrWhiteSpace(_openAi.ApiKey))
        {
            try
            {
                return await CallOpenAiVisionAsync(listing.Category.Name, imageUrls, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI fotoğraf analizi başarısız, fallback kullanılıyor.");
            }
        }

        return BuildFallbackAnalysis(imageUrls.Count);
    }

    private async Task<PhotoAnalysisDto> CallOpenAiVisionAsync(string categoryName, List<string> imageUrls, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var system =
            "Sen bir araç ve gayrimenkul fotoğraf analisti uzmanısın. " +
            "Sana verilen ilan fotoğraflarını dikkatle incele.\n\n" +
            "=== ÇIKTI FORMATI (SADECE JSON) ===\n" +
            "{\n" +
            "  \"overallConditionScore\": 0-100,\n" +
            "  \"conditionLabel\": \"Çok İyi\" | \"İyi\" | \"Orta\" | \"Kötü\",\n" +
            "  \"hasScratchOrDent\": true|false,\n" +
            "  \"hasPaintDifference\": true|false,\n" +
            "  \"suspectedTaxiOrRental\": true|false,\n" +
            "  \"interiorDamage\": true|false,\n" +
            "  \"findings\": [\"<tespit 1>\", \"<tespit 2>\", ...],\n" +
            "  \"warnings\": [\"<uyarı 1>\", ...],\n" +
            "  \"summary\": \"<2-3 cümle Türkçe özet>\"\n" +
            "}\n\n" +
            "=== PUANLAMA ===\n" +
            "- 85-100: Kusursuz, sıfır veya sıfır gibi\n" +
            "- 70-84: Çok iyi, küçük izler\n" +
            "- 50-69: İyi, bazı yıpranmalar\n" +
            "- 30-49: Orta, belirgin hasar/yıpranma\n" +
            "- 0-29: Kötü, ciddi hasar\n\n" +
            "=== KONTROL LİSTESİ ===\n" +
            "ARAÇ için: Kaporta düzgünlüğü, boya uyumu, cam çizikleri, jant hasarı, " +
            "iç döşeme yıpranması, tavan halısı lekesi, km teydi için gösterge, " +
            "taksi sarısı ya da kiralık araç izleri (yazı, çıkarılmış sticker izi, plastik ayraç yeri).\n" +
            "EMLAK için: Duvar çatlakları, nem lekesi, boya dökülmesi, döşeme hasarı, " +
            "tavan sarkması, pencere çerçevesi çürümesi, banyo/mutfak yıpranması.\n" +
            "GENEL: Profesyonel fotoğraf mı yoksa acelece çekilmiş mi? " +
            "Kasıtlı gizlenen alan var mı (lens açısı, karanlık köşeler)?";

        // Content array: önce text, sonra her fotoğraf
        var contentItems = new List<object>
        {
            new { type = "text", text = $"Kategori: {categoryName}\nAşağıdaki {imageUrls.Count} fotoğrafı analiz et:" }
        };

        foreach (var url in imageUrls)
        {
            contentItems.Add(new
            {
                type = "image_url",
                image_url = new { url, detail = "low" }
            });
        }

        var body = new
        {
            model = _openAi.Model,
            max_tokens = 1000,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = contentItems }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, raw);

        var root = JsonDocument.Parse(raw).RootElement;
        var rawContent = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";

        // Model bazen JSON'u markdown code block içine alabilir, temizle
        var jsonContent = rawContent.Trim();
        if (jsonContent.StartsWith("```"))
        {
            var start = jsonContent.IndexOf('{');
            var end = jsonContent.LastIndexOf('}');
            if (start >= 0 && end > start)
                jsonContent = jsonContent[start..(end + 1)];
        }

        var doc = JsonDocument.Parse(jsonContent).RootElement;

        var score = doc.TryGetProperty("overallConditionScore", out var sc) && sc.TryGetInt32(out var scv) ? Math.Clamp(scv, 0, 100) : 50;
        var label = doc.TryGetProperty("conditionLabel", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString()! : ConditionLabelFor(score);
        var hasScratch = doc.TryGetProperty("hasScratchOrDent", out var hs) && hs.ValueKind == JsonValueKind.True;
        var hasPaint = doc.TryGetProperty("hasPaintDifference", out var hp) && hp.ValueKind == JsonValueKind.True;
        var suspectedTaxi = doc.TryGetProperty("suspectedTaxiOrRental", out var st) && st.ValueKind == JsonValueKind.True;
        var interiorDmg = doc.TryGetProperty("interiorDamage", out var id) && id.ValueKind == JsonValueKind.True;

        var findings = new List<string>();
        if (doc.TryGetProperty("findings", out var fa) && fa.ValueKind == JsonValueKind.Array)
            foreach (var item in fa.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    findings.Add(item.GetString()!);

        var warnings = new List<string>();
        if (doc.TryGetProperty("warnings", out var wa) && wa.ValueKind == JsonValueKind.Array)
            foreach (var item in wa.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    warnings.Add(item.GetString()!);

        var summary = doc.TryGetProperty("summary", out var su) && su.ValueKind == JsonValueKind.String ? su.GetString()! : "";

        return new PhotoAnalysisDto(score, label, hasScratch, hasPaint, suspectedTaxi, interiorDmg, findings, warnings, summary);
    }

    private static PhotoAnalysisDto BuildFallbackAnalysis(int imageCount)
    {
        return new PhotoAnalysisDto(
            OverallConditionScore: 50,
            ConditionLabel: "Orta",
            HasScratchOrDent: false,
            HasPaintDifference: false,
            SuspectedTaxiOrRental: false,
            InteriorDamage: false,
            Findings: [$"{imageCount} fotoğraf mevcut, görsel analiz servisi şu an kullanılamıyor."],
            Warnings: [],
            Summary: "AI analiz servisi geçici olarak kullanılamıyor. Lütfen fotoğrafları kendiniz inceleyin."
        );
    }

    private static string ConditionLabelFor(int score) => score switch
    {
        >= 85 => "Çok İyi",
        >= 70 => "İyi",
        >= 50 => "Orta",
        _ => "Kötü"
    };
}
