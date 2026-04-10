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

public sealed class AiSalePredictionService : IAiSalePredictionService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _openAi;
    private readonly ILogger<AiSalePredictionService> _logger;

    public AiSalePredictionService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IOptions<OpenAiOptions> openAi,
        ILogger<AiSalePredictionService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
        _logger = logger;
    }

    public async Task<SalePredictionDto> PredictAsync(Guid listingId, CancellationToken ct = default)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.AttributeValues)
                .ThenInclude(v => v.CategoryAttribute)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == listingId, ct)
            ?? throw new InvalidOperationException("İlan bulunamadı.");

        var imageCount = listing.Images.Count;
        var filledAttrs = listing.AttributeValues
            .Where(v => !string.IsNullOrWhiteSpace(v.ValueText) || v.ValueInt.HasValue || v.ValueDecimal.HasValue || v.ValueBool.HasValue)
            .Select(v => $"{v.CategoryAttribute?.DisplayName ?? v.CategoryAttribute?.AttributeKey}: {v.ValueText ?? v.ValueInt?.ToString() ?? v.ValueDecimal?.ToString() ?? v.ValueBool?.ToString()}")
            .ToList();

        var listingAge = listing.PublishedAt.HasValue
            ? (int)(DateTimeOffset.UtcNow - listing.PublishedAt.Value).TotalDays
            : 0;

        var listingTypeLabel = listing.ListingType.ToString() switch
        {
            "Satilik" => "Satılık",
            "Kiralik" => "Kiralık",
            "DevrenSatilik" => "Devren Satılık",
            "DevrenKiralik" => "Devren Kiralık",
            _ => listing.ListingType.ToString()
        };

        var system =
            "Sen Türkiye'nin önde gelen emlak ve araç ilan sitelerinde (sahibinden.com, arabam.com, emlakjet.com) " +
            "uzman bir pazar analisti olarak görev yapıyorsun.\n\n" +
            "Sana verilen ilan bilgilerine göre gerçekçi ve pazar verilerine dayalı tahminler üret.\n\n" +
            "=== ÇIKTI FORMATI (SADECE JSON) ===\n" +
            "{\n" +
            "  \"score\": 0-100,\n" +
            "  \"scoreLabel\": \"Yüksek\" | \"Orta\" | \"Düşük\",\n" +
            "  \"estimatedDays\": <kaç günde satılır/kiralanır (tam sayı)>,\n" +
            "  \"estimatedViews7d\": <7 günde tahmini görüntüleme sayısı>,\n" +
            "  \"estimatedMessages7d\": <7 günde tahmini mesaj sayısı>,\n" +
            "  \"priceTip\": \"<Türkçe öneri cümlesi, örn: 'Fiyatı 20.000 TL düşürürsen 2x hızlı satılır'>\" | null,\n" +
            "  \"priceDelta\": <öneri fiyat değişimi TL cinsinden, negatif=indir, null=değişiklik gerekmez>,\n" +
            "  \"speedFactor\": <priceTip'deki hız çarpanı (örn: 2.0), null if no tip>,\n" +
            "  \"reasoning\": \"<2 cümlelik kısa Türkçe analiz>\"\n" +
            "}\n\n" +
            "=== PUANLAMA KILAVUZU ===\n" +
            "- 80-100: Fiyat piyasaya uygun, fotoğraf var, açıklama dolu, yüksek talep kategorisi → 1-2 haftada satılır\n" +
            "- 50-79: Birkaç eksik var, fiyat biraz yüksek → 1-2 ayda satılır\n" +
            "- 0-49: Fiyat çok yüksek veya ilan eksik → 3+ ay veya hiç satılmaz\n\n" +
            "=== PAZAR BİLGİSİ ===\n" +
            "- Araç ilanları: Türkiye'de ortalama otomobil ilanı 15-45 günde satılır. Fiyat ±%10 etkisi büyüktür.\n" +
            "- Emlak Satılık: Ortalama 60-120 gün. Konum ve metrekare kritik.\n" +
            "- Emlak Kiralık: Ortalama 7-21 gün. Talep çok yüksek.\n" +
            "- Her %10 fiyat indirimi → yaklaşık 1.3-1.5x hızlanma sağlar.\n" +
            "- Fotoğrafsız ilanlar %60 daha az görüntüleme alır.\n" +
            "- Eksik özellik her biri skoru -3 düşürür.";

        var userContent =
            $"KATEGORİ: {listing.Category.Name}\n" +
            $"İLAN TÜRÜ: {listingTypeLabel}\n" +
            $"BAŞLIK: {listing.Title}\n" +
            $"AÇIKLAMA UZUNLUĞU: {listing.Description.Length} karakter\n" +
            $"FİYAT: {(listing.Price.HasValue ? $"{listing.Price:N0} {listing.Currency}" : "Belirtilmemiş")}\n" +
            $"FOTOĞRAF SAYISI: {imageCount}\n" +
            $"DOLU ÖZELLİK SAYISI: {filledAttrs.Count}\n" +
            $"ÖZELLİKLER: {(filledAttrs.Count > 0 ? string.Join(", ", filledAttrs.Take(10)) : "Yok")}\n" +
            $"MEVCUT GÖRÜNTÜLEME: {listing.ViewCount}\n" +
            $"İLAN YAŞI: {listingAge} gün";

        if (!string.IsNullOrWhiteSpace(_openAi.ApiKey))
        {
            try
            {
                return await CallOpenAiAsync(system, userContent, listing.Price, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI satış tahmini başarısız, fallback kullanılıyor.");
            }
        }

        return BuildFallbackPrediction(imageCount, filledAttrs.Count, listing.Price, listing.Category.Name);
    }

    private async Task<SalePredictionDto> CallOpenAiAsync(string system, string userContent, decimal? price, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = userContent }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, raw);

        var root = JsonDocument.Parse(raw).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var doc = JsonDocument.Parse(content).RootElement;

        var score = doc.TryGetProperty("score", out var sc) && sc.TryGetInt32(out var scv) ? Math.Clamp(scv, 0, 100) : 50;
        var scoreLabel = doc.TryGetProperty("scoreLabel", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString()! : ScoreLabelFor(score);
        var days = doc.TryGetProperty("estimatedDays", out var ed) && ed.TryGetInt32(out var edv) ? edv : 30;
        var views = doc.TryGetProperty("estimatedViews7d", out var ev) && ev.TryGetInt32(out var evv) ? evv : 50;
        var msgs = doc.TryGetProperty("estimatedMessages7d", out var em) && em.TryGetInt32(out var emv) ? emv : 2;
        var priceTip = doc.TryGetProperty("priceTip", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
        decimal? priceDelta = null;
        if (doc.TryGetProperty("priceDelta", out var pd) && pd.ValueKind == JsonValueKind.Number && pd.TryGetDecimal(out var pdv)) priceDelta = pdv;
        double? speedFactor = null;
        if (doc.TryGetProperty("speedFactor", out var sf) && sf.ValueKind == JsonValueKind.Number && sf.TryGetDouble(out var sfv)) speedFactor = sfv;
        var reasoning = doc.TryGetProperty("reasoning", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString()! : "";

        return new SalePredictionDto(score, scoreLabel, days, views, msgs, priceTip, priceDelta, speedFactor, reasoning);
    }

    private static SalePredictionDto BuildFallbackPrediction(int imageCount, int attrCount, decimal? price, string categoryName)
    {
        var score = 60;
        if (imageCount == 0) score -= 20;
        if (attrCount < 3) score -= 15;
        if (price == null) score -= 10;
        score = Math.Clamp(score, 10, 85);

        return new SalePredictionDto(
            Score: score,
            ScoreLabel: ScoreLabelFor(score),
            EstimatedDays: score >= 70 ? 14 : score >= 50 ? 45 : 90,
            EstimatedViews7d: imageCount > 0 ? 80 : 30,
            EstimatedMessages7d: score >= 70 ? 5 : 2,
            PriceTip: null,
            PriceDelta: null,
            SpeedFactor: null,
            Reasoning: $"{categoryName} kategorisindeki ilanınız analiz edildi."
        );
    }

    private static string ScoreLabelFor(int score) => score >= 70 ? "Yüksek" : score >= 40 ? "Orta" : "Düşük";
}
