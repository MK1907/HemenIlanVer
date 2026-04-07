using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

/// <summary>
/// Arka plan işçisi: kuyruktaki kategori ID'lerini işleyerek
/// o kategorinin Enum attribute'larının option değerlerini AI ile zenginleştirir.
/// </summary>
public sealed class CategoryEnrichmentWorker(
    ICategoryEnrichmentQueue queue,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<CategoryEnrichmentWorker> logger)
    : BackgroundService
{
    private readonly OpenAiOptions _openAi = openAiOptions.Value;

    // Son N dakika içinde işlenmiş kategori ID'leri — tekrar işlemeyi önler
    private readonly HashSet<Guid> _recentlyProcessed = [];
    private static readonly TimeSpan ReprocessCooldown = TimeSpan.FromMinutes(10);
    private readonly Dictionary<Guid, DateTimeOffset> _processedAt = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CategoryEnrichmentWorker başladı.");

        // Başlangıçta: eksik Enum seçenekleri olan tüm kategorileri kuyruğa ekle
        await EnqueueStartupCategoriesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await queue.DequeueAsync(stoppingToken);
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Enrichment worker beklenmedik hata.");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task EnqueueStartupCategoriesAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var allEnumAttrs = await db.CategoryAttributes
                .Include(a => a.Options)
                .Where(a => a.DataType == AttributeDataType.Enum)
                .ToListAsync(ct);

            var categoryIds = allEnumAttrs
                .Where(a => a.Options.Count < 30)
                .Select(a => a.CategoryId)
                .Distinct()
                .ToList();

            foreach (var id in categoryIds)
                queue.Enqueue(new CategoryEnrichmentJob(id));

            logger.LogInformation("Başlangıç zenginleştirme: {Count} kategori kuyruğa eklendi.", categoryIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Başlangıç kuyruğa ekleme hatası, atlanıyor.");
        }
    }

    private async Task ProcessAsync(CategoryEnrichmentJob job, CancellationToken ct)
    {
        var categoryId = job.CategoryId;

        // Cooldown: son 10 dakika içinde işlendiyse atla
        // (Ancak job'da yeni tespit edilen değerler varsa cooldown'u yok say — kullanıcı aktif)
        if (job.DetectedValues is null or { Count: 0 })
        {
            if (_processedAt.TryGetValue(categoryId, out var last)
                && DateTimeOffset.UtcNow - last < ReprocessCooldown)
            {
                logger.LogDebug("Kategori {Id} son {Min} dk içinde işlendi, atlanıyor.", categoryId, ReprocessCooldown.TotalMinutes);
                return;
            }
        }

        logger.LogInformation("Kategori {Id} zenginleştiriliyor… (tespit: {Vals})",
            categoryId,
            job.DetectedValues is { Count: > 0 }
                ? string.Join(", ", job.DetectedValues.Select(kv => $"{kv.Key}={kv.Value}"))
                : "startup");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null) return;

        var attrs = await db.CategoryAttributes
            .Include(x => x.Options)
            .Where(x => x.CategoryId == categoryId && x.DataType == AttributeDataType.Enum)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        if (attrs.Count == 0) return;

        var client = httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var enriched = 0;
        foreach (var attr in attrs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // ── Child attribute (ParentAttributeId var, örn: model → brand) ──────────────
                if (attr.ParentAttributeId.HasValue)
                {
                    var parentAttr = await db.CategoryAttributes
                        .Include(x => x.Options)
                        .FirstOrDefaultAsync(a => a.Id == attr.ParentAttributeId.Value, ct);

                    if (parentAttr is null || parentAttr.Options.Count == 0) continue;

                    // Tespit edilen parent değerini öne al (örn: AI "Opel" dediyse Opel önce)
                    var detectedParentValue = job.DetectedValues is not null
                        && parentAttr.AttributeKey is not null
                        && job.DetectedValues.TryGetValue(parentAttr.AttributeKey, out var dpv)
                        ? dpv : null;

                    var orderedParentOpts = parentAttr.Options
                        .OrderBy(o => detectedParentValue != null
                            && string.Equals(o.ValueKey, detectedParentValue, StringComparison.OrdinalIgnoreCase)
                            ? 0 : 1)
                        .ToList();

                    // Her parent option (marka) için ayrı AI çağrısı
                    foreach (var parentOpt in orderedParentOpts)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Bu parent seçeneğine ait zaten yeterli child var mı?
                        var existingForParent = attr.Options
                            .Count(o => o.ParentOptionId == parentOpt.Id);
                        if (existingForParent >= 15) continue;

                        var childOptions = await FetchChildOptionsFromAiAsync(
                            client, category.Name, parentOpt.Label, attr, ct);

                        if (childOptions.Count == 0) continue;

                        var existingChildKeys = attr.Options
                            .Where(o => o.ParentOptionId == parentOpt.Id)
                            .Select(o => o.ValueKey)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var toAdd = childOptions
                            .Where(o => !existingChildKeys.Contains(o.ValueKey))
                            .ToList();

                        if (toAdd.Count == 0) continue;

                        var maxSort = attr.Options.Count > 0 ? attr.Options.Max(o => o.SortOrder) : 0;
                        foreach (var opt in toAdd)
                        {
                            var newOpt = new CategoryAttributeOption
                            {
                                Id = Guid.NewGuid(),
                                CategoryAttributeId = attr.Id,
                                ValueKey = opt.ValueKey,
                                Label = opt.Label,
                                SortOrder = ++maxSort,
                                ParentOptionId = parentOpt.Id,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            db.CategoryAttributeOptions.Add(newOpt);
                            attr.Options.Add(newOpt); // in-memory güncel tut
                            enriched++;
                        }

                        await db.SaveChangesAsync(ct);
                        logger.LogInformation(
                            "'{Cat}' / '{Attr}' / '{Parent}': {Count} model eklendi.",
                            category.Name, attr.DisplayName, parentOpt.Label, toAdd.Count);

                        await Task.Delay(300, ct); // rate limit
                    }
                    continue; // bu attr işlendi, diğerine geç
                }

                // ── Root attribute (ParentAttributeId yok, örn: brand, fuel, gear) ──────────
                if (attr.Options.Count >= 30) continue;

                var newOptions = await FetchOptionsFromAiAsync(client, category.Name, attr, ct);
                if (newOptions.Count == 0) continue;

                var existingKeys = attr.Options
                    .Select(o => o.ValueKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var rootToAdd = newOptions
                    .Where(o => !existingKeys.Contains(o.ValueKey))
                    .ToList();

                if (rootToAdd.Count == 0) continue;

                var rootMaxSort = attr.Options.Count > 0 ? attr.Options.Max(o => o.SortOrder) : 0;
                foreach (var opt in rootToAdd)
                {
                    db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                    {
                        Id = Guid.NewGuid(),
                        CategoryAttributeId = attr.Id,
                        ValueKey = opt.ValueKey,
                        Label = opt.Label,
                        SortOrder = ++rootMaxSort,
                        ParentOptionId = null,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    enriched++;
                }

                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Kategori '{Cat}' / '{Attr}': {Count} yeni option eklendi.",
                    category.Name, attr.DisplayName, rootToAdd.Count);

                await Task.Delay(300, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Attribute '{Attr}' zenginleştirme hatası, atlanıyor.", attr.AttributeKey);
            }
        }

        _processedAt[categoryId] = DateTimeOffset.UtcNow;
        logger.LogInformation("Kategori {Id} zenginleştirmesi tamamlandı. Toplam {N} yeni option.", categoryId, enriched);
    }

    /// <summary>
    /// Belirli bir parent option (örn: Ford markası) için child option listesi (modeller) çeker.
    /// </summary>
    private async Task<List<(string ValueKey, string Label)>> FetchChildOptionsFromAiAsync(
        HttpClient client,
        string categoryName,
        string parentLabel,
        CategoryAttribute attr,
        CancellationToken ct)
    {
        var system =
            $"Sen Türkiye ilan sitelerinde '{categoryName}' kategorisi uzmanısın.\n" +
            $"'{parentLabel}' için '{attr.DisplayName}' değerlerini JSON formatında üret.\n\n" +
            "KURALLAR:\n" +
            $"- Sadece gerçek '{parentLabel}' {attr.DisplayName} adlarını yaz.\n" +
            "- Üretici/marka adı YAZMA — sadece model/alt-kategori isimleri.\n" +
            "- valueKey: slug formatı (küçük harf, tire), label: orijinal yazım.\n" +
            "- Yaygın bilinen, gerçek değerleri üret. Uydurma ekleme.\n\n" +
            "JSON çıktısı: {\"options\":[{\"valueKey\":\"corsa\",\"label\":\"Corsa\"}, ...]}";

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = $"'{parentLabel}' için tüm {attr.DisplayName} listesini JSON olarak ver." }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("FetchChildOptions HTTP {Code} ({Parent}): {Body}",
                (int)resp.StatusCode, parentLabel, raw[..Math.Min(200, raw.Length)]);
            return [];
        }

        var root = JsonDocument.Parse(raw).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var doc = JsonDocument.Parse(content).RootElement;

        var result = new List<(string, string)>();
        if (!doc.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("FetchChildOptions: 'options' key yok ({Parent}). Content: {C}",
                parentLabel, content[..Math.Min(200, content.Length)]);
            return result;
        }

        foreach (var item in opts.EnumerateArray())
        {
            var vk = item.TryGetProperty("valueKey", out var vkp) ? vkp.GetString() : null;
            var lb = item.TryGetProperty("label", out var lbp) ? lbp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(vk) && !string.IsNullOrWhiteSpace(lb))
                result.Add((vk, lb));
        }

        return result;
    }

    private async Task<List<(string ValueKey, string Label, string? ParentValue)>> FetchOptionsFromAiAsync(
        HttpClient client,
        string categoryName,
        CategoryAttribute attr,
        CancellationToken ct)
    {
        var existingSample = attr.Options
            .Take(10)
            .Select(o => o.ValueKey)
            .ToList();

        var parentInfo = attr.ParentAttributeId.HasValue
            ? " (Bu alan başka bir alana bağımlıdır; parentValue olarak parent seçeneğinin valueKey'ini yaz.)"
            : "";

        // Attribute anahtarına göre ne tür değer beklediğimizi belirt (generic, kategori bağımsız)
        var attrKey = (attr.AttributeKey ?? "").ToLowerInvariant();
        var attrHint = attrKey switch
        {
            "brand" or "marka" =>
                $"'{categoryName}' kategorisinde Türkiye'de satılan ürünlerin gerçek ÜRETİCİ/MARKA adları. " +
                "Sadece fabrika markası (üretici firma adı) — model adı, donanım paketi veya ürün serisi EKLEME.",
            "model" =>
                $"'{categoryName}' kategorisine ait gerçek ürün MODEL adları. " +
                "Sadece model ismi — marka adı (üretici firma) EKLEME.",
            "fuel" or "yakıt" or "yakitturu" or "yakıttürü" or "fueltype" or "fuel-type" or "yakit-turu" =>
                $"'{categoryName}' kategorisinde Türkiye son kullanıcı pazarında YAYGINCA kullanılan yakıt/enerji tipleri. " +
                "İzin verilenler: Benzin, Dizel, LPG (Otogaz), Elektrik, Hibrit (Benzin+Elektrik), Plug-in Hibrit, Doğalgaz (CNG). " +
                "Metanol, hidrojen, biyodizel, etanol gibi endüstriyel/nadir yakıt tipleri KESİNLİKLE EKLEME.",
            "gear" or "vites" or "transmission" or "şanzıman" or "sanziman" or "geartype" =>
                $"'{categoryName}' kategorisinde kullanılan şanzıman/vites türleri. " +
                "İzin verilenler: Manuel, Otomatik, Yarı-Otomatik / Çift Kavramalı (DSG/PDK/DCT). " +
                "Başka kategori ekleme.",
            "bodytype" or "body" or "kasa" or "kasatipi" or "kasa-tipi" or "bodytype" =>
                $"'{categoryName}' kategorisinde Türkiye pazarında yaygın kullanılan araç kasa tipleri. " +
                "İzin verilenler: Sedan, Hatchback (3 veya 5 kapı), SUV/Crossover, Coupe, Cabrio/Roadster, " +
                "Station Wagon/Kombi, MPV/Minivan, Pickup. Nadir/uydurma tipler EKLEME.",
            "color" or "renk" =>
                $"'{categoryName}' için Türkiye pazarında yaygın araç renkleri. " +
                "Standart renk isimleri (Beyaz, Siyah, Gri, Gümüş, Kırmızı, Mavi, Yeşil, Kahverengi, Bej, Turuncu, Sarı). " +
                "Nadir, metalik alt varyasyonlar gibi değerler EKLEME — sadece ana renkler.",
            "damage" or "hasar" or "tramer" =>
                $"'{categoryName}' kategorisinde araç hasar/tramer durumu için standart değerler. " +
                "İzin verilenler: Hasarsız/Boyasız, Lokal Boyalı, Değişen Parça Var, Ağır Hasarlı/Hurda. " +
                "Başka değer ekleme.",
            _ =>
                $"'{categoryName}' kategorisinde '{attr.DisplayName}' özelliği için " +
                "Türkiye son kullanıcı pazarında YAYGINCA kullanılan, gerçek ve bilinen değerler. " +
                "Endüstriyel, nadir, egzotik veya anlamsız değerler KESİNLİKLE EKLEME."
        };

        var system =
            $"Sen Türkiye ilan sitelerinde '{categoryName}' kategorisi uzmanısın.\n" +
            $"Görev: {attrHint}{parentInfo}\n\n" +
            "KURALLAR:\n" +
            "- Sadece gerçek, bilinen, sektörde yaygın kullanılan değerleri JSON formatında üret.\n" +
            "- Uydurma, anlamsız, çok nadir, endüstriyel veya yarış amaçlı değer KESİNLİKLE EKLEME.\n" +
            "- valueKey: slug formatı (küçük harf, Türkçe karakter KULLANMA, boşluk yerine tire), label: orijinal doğal yazım.\n" +
            $"- Zaten mevcut olanlar (bunları tekrarlama): [{string.Join(", ", existingSample)}]\n\n" +
            "JSON çıktısı: {\"options\":[{\"valueKey\":\"apple\",\"label\":\"Apple\",\"parentValue\":null}, ...]}";

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = $"'{categoryName}' kategorisinde '{attr.DisplayName}' için kapsamlı option listesi üret." }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("FetchOptions HTTP {Code}: {Body}", (int)resp.StatusCode, raw[..Math.Min(300, raw.Length)]);
            return [];
        }

        var root = JsonDocument.Parse(raw).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        logger.LogDebug("FetchOptions raw content for '{Cat}/{Attr}': {Content}", categoryName, attr.AttributeKey, content[..Math.Min(500, content.Length)]);
        var doc = JsonDocument.Parse(content).RootElement;

        var result = new List<(string, string, string?)>();
        if (!doc.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("FetchOptions: 'options' key bulunamadı. Content: {Content}", content[..Math.Min(300, content.Length)]);
            return result;
        }

        foreach (var item in opts.EnumerateArray())
        {
            var vk = item.TryGetProperty("valueKey", out var vkp) ? vkp.GetString() : null;
            var lb = item.TryGetProperty("label", out var lbp) ? lbp.GetString() : null;
            var pv = item.TryGetProperty("parentValue", out var pvp) && pvp.ValueKind == JsonValueKind.String
                ? pvp.GetString() : null;

            if (!string.IsNullOrWhiteSpace(vk) && !string.IsNullOrWhiteSpace(lb))
                result.Add((vk, lb, pv));
        }

        return result;
    }
}
