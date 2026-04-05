using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
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

        var rootList = roots.Select(r => new { r.Name, r.Slug }).ToList();
        var childList = children.Select(c => new
        {
            c.Name,
            c.Slug,
            c.ParentId,
            parentSlug = roots.FirstOrDefault(r => r.Id == c.ParentId)?.Slug
        }).ToList();

        var system =
            "Sen Türkiye'deki sahibinden.com / letgo benzeri ilan sitelerinde ÜRÜN KATEGORİSİ TESPİTİ yapan bir uzmansın.\n\n" +

            "=== ADIM ADIM DÜŞÜN ===\n" +
            "1. Önce kullanıcının metninde geçen ÜRÜNü / HİZMETi belirle.\n" +
            "2. Bu ürün hangi sektöre/kategoriye aittir? (örn: çanta, ayakkabı, giysi → Giyim & Aksesuar; telefon, laptop → Elektronik; araba → Araç; ev, daire → Emlak)\n" +
            "3. Mevcut kategorilerden en uygun olanı seç. Yoksa bootstrap ile yeni kategori oluştur.\n\n" +

            "=== KRİTİK KURALLAR ===\n" +
            "- Marka adları (Prada, Gucci, Nike, Adidas, Louis Vuitton, Chanel vb.) ürünün KATEGORİSİNİ DEĞİŞTİRMEZ. Marka = attribute, kategori değil.\n" +
            "- Bir çanta elektronik DEĞİLDİR. Bir ayakkabı araç DEĞİLDİR. ÜRÜNÜN FİZİKSEL DOĞASINA bak.\n" +
            "- \"Orijinal\", \"replika\", \"toptan\" gibi kelimeler kategoriyi değiştirmez, ürünün kendisine odaklan.\n\n" +

            "=== ÖRNEK EŞLEŞTIRMELER ===\n" +
            "- \"Çanta Orijinal Prada Marka\" → Giyim & Aksesuar > Çanta (marka: Prada)\n" +
            "- \"2012 model Fiat Egea\" → Araç > Otomobil (marka: Fiat, model: Egea, yıl: 2012)\n" +
            "- \"iPhone 15 Pro 256GB\" → Elektronik > Cep Telefonu (marka: Apple, model: iPhone 15 Pro)\n" +
            "- \"3+1 daire Kadıköy\" → Emlak > Konut\n" +
            "- \"LGS Matematik özel ders\" → Eğitim > Özel Ders\n" +
            "- \"Nike Air Force beyaz spor ayakkabı\" → Giyim & Aksesuar > Ayakkabı\n" +
            "- \"Toptan havlu seti\" → Giyim & Aksesuar > Tekstil veya Ev & Yaşam > Ev Tekstili\n\n" +

            "=== MEVCUT KATEGORİLER ===\n" +
            "Ana kategoriler (name → slug): " + JsonSerializer.Serialize(rootList) + "\n" +
            "Alt kategoriler (name → slug, parentSlug): " + JsonSerializer.Serialize(childList) + "\n\n" +

            "=== ÇIKTI FORMATI (SADECE JSON) ===\n" +
            "{\"reasoning\":\"kısa düşünce (ürün nedir, hangi kategoriye ait)\", \"rootSlug\":\"...\", \"suggestedChildSlug\":\"...\"|null, \"confidence\":0.0-1.0, " +
            "\"bootstrap\":{\"needed\":true/false, \"rootName\":\"...\", \"rootSlug\":\"...\", \"childName\":\"...\", \"childSlug\":\"...\", " +
            "\"filters\":[{\"key\":\"...\", \"displayName\":\"...\", \"dataType\":\"String|Int|Decimal|Bool|Enum|Money\", \"required\":true/false, " +
            "\"parentKey\":null|\"başka bir filtre key'i (bağımlılık varsa)\", " +
            "\"options\":[{\"valueKey\":\"...\",\"label\":\"...\",\"parentValue\":null|\"parent option valueKey\"}]}]}}\n\n" +

            "=== BOOTSTRAP KURALLARI ===\n" +
            "- Mevcut slug'lardan biri uygunsa → bootstrap.needed=false, o slug'ı seç.\n" +
            "- Uygun kategori YOKSA → bootstrap.needed=true, doğru isim ve slug ile yeni oluştur.\n" +
            "- TÜRKÇE isimler kullan, slug'lar küçük harf ve tire.\n\n" +

            "=== FİLTRE (ATTRIBUTE) KURALLARI ===\n" +
            "Yeni kategori oluştururken, o ürün/hizmet türünde PIYASADA KULLANILAN TÜM ÖZELLİKLERİ filters dizisine ekle.\n" +
            "Sahibinden.com, Hepsiburada, Trendyol gibi sitelerde o kategoride hangi filtreler varsa HEPSİNİ koy.\n" +
            "En az 10, en fazla 20 filtre üret. İlk sıraya en önemli olanları koy.\n" +
            "Enum için options en az 3-4 seçenek koy (gerçekçi).\n\n" +

            "=== BAĞIMLILIK (PARENT-CHILD) KURALLARI ===\n" +
            "Bazı özellikler başka bir özelliğe BAĞIMLIDIR. Örneğin 'model' → 'marka'ya bağlıdır.\n" +
            "Bağımlı bir filtre tanımlarken parentKey alanına bağlı olduğu filtrenin key'ini yaz.\n" +
            "Bağımlı filtrenin her option'ında parentValue alanına, o option'ın hangi parent seçeneğine ait olduğunu yaz.\n" +
            "Örnek: marka→BMW altında model→320i, 520d; marka→Mercedes altında model→C200, E220.\n" +
            "Bağımlılık yoksa parentKey ve parentValue null/yok olsun.\n\n" +

            "KATEGORİ BAZLI FİLTRE ÖRNEKLERİ (referans — bunlarla sınırlı kalma, eksiksiz ol):\n" +
            "Otomobil: marka, model, yıl, km, vites, yakıt, kasaTipi, motorHacmi, beygir, renk, çekiş, plaka, hasarDurumu, boyaDeğişen, garanti, takasUygun, kimden\n" +
            "Konut: odaSayısı, m2, binaYaşı, kat, toplamKat, ısıtma, banyo, balkon, esyalı, siteMi, otopark, cephe, tapuDurumu, kimden\n" +
            "Cep Telefonu: marka, model, hafıza, ram, ekranBoyutu, renk, garanti, durum, kutuVarMi, kimden\n" +
            "Çanta: marka, tip, malzeme, renk, boyut, cinsiyet, durum, orijinallik, kutuSertifika, kimden\n" +
            "Ayakkabı: marka, model, numara, renk, cinsiyet, tip, malzeme, durum, kimden\n" +
            "Özel Ders: branş, hedefSınav, format, seviye, tecrübe, konum, kimden";

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

        var rootCat = roots.FirstOrDefault(r => r.Slug == rootSlug)
            ?? roots.FirstOrDefault(r => CategorySlugHelper.SlugEquals(r.Slug, rootSlug));
        if (rootCat is null)
            throw new InvalidOperationException(
                $"OpenAI kategori yanıtı veritabanıyla eşleşmedi (rootSlug: {rootSlug}).");

        var subs = children.Where(c => c.ParentId == rootCat.Id)
            .Select(c => new SubCategoryOptionDto(c.Id, c.Name, c.Slug))
            .ToList();

        Guid? leafId = null;
        if (!string.IsNullOrEmpty(childSlug))
        {
            var match = children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == childSlug)
                ?? children.FirstOrDefault(c => c.ParentId == rootCat.Id && CategorySlugHelper.SlugEquals(c.Slug, childSlug));
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

                if (sugAttrs is { Count: > 0 })
                    await PersistNewOptionValuesAsync(leafId.Value, sugAttrs, ct);
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
            "Sen Türkiye ilan sitelerinde ilan metni ayrıştırıcısın.\n" +
            "Kullanıcının yazdığı metne göre aşağıdaki kategori filtre alanlarının değerlerini çıkar.\n\n" +
            "ALANLAR: " + JsonSerializer.Serialize(attrSpec) + "\n\n" +
            "KURALLAR:\n" +
            "- Enum tipli alanlar: Eğer verilen options içinde uygun bir değer varsa onu seç. Yoksa metinden çıkardığın gerçek değeri yaz (yeni değer olabilir).\n" +
            "- String tipli alanlar: Metinden çıkardığın değeri serbest yaz.\n" +
            "- Int / Decimal / Money → sadece sayı.\n" +
            "- Bool → true / false.\n" +
            "- Metinden DOĞRUDAN veya MANTIKSAL ÇIKARIM ile belirleyebildiğin tüm alanları doldur.\n" +
            "  Örn: 'Prada çanta' → marka: Prada. 'Otomatik vites' → vites: Otomatik. '2012 model' → yıl: 2012.\n" +
            "- Metinden çıkaramadığın alanları JSON'a KOYma.\n" +
            "- suggestedTitle: Türkçe, çekici, kısa ilan başlığı (maks 100 karakter).\n" +
            "- suggestedDescription: 2-3 cümle detaylı açıklama.\n" +
            "- suggestedPrice: TRY, tahmin edebiliyorsan; edemezsen null.\n\n" +
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

    private async Task PersistNewOptionValuesAsync(
        Guid leafCategoryId,
        IReadOnlyDictionary<string, string> extractedValues,
        CancellationToken ct)
    {
        try
        {
            var attrs = await _db.CategoryAttributes
                .Include(x => x.Options)
                .Where(x => x.CategoryId == leafCategoryId)
                .ToListAsync(ct);

            var added = 0;
            var newOptionMap = new Dictionary<(Guid attrId, string value), Guid>();

            foreach (var (key, value) in extractedValues)
            {
                if (string.IsNullOrWhiteSpace(value) || value is "true" or "false")
                    continue;

                var attr = attrs.FirstOrDefault(a =>
                    string.Equals(a.AttributeKey, key, StringComparison.OrdinalIgnoreCase));
                if (attr is null) continue;
                if (attr.DataType is AttributeDataType.Bool or AttributeDataType.Money
                    or AttributeDataType.Decimal or AttributeDataType.Int)
                    continue;

                var existing = attr.Options.FirstOrDefault(o =>
                    string.Equals(o.ValueKey, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(o.Label, value, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    newOptionMap[(attr.Id, value)] = existing.Id;
                    continue;
                }

                Guid? parentOptId = null;
                if (attr.ParentAttributeId.HasValue)
                {
                    var parentAttr = attrs.FirstOrDefault(a => a.Id == attr.ParentAttributeId.Value);
                    if (parentAttr is not null)
                    {
                        var parentKey = parentAttr.AttributeKey;
                        if (extractedValues.TryGetValue(parentKey, out var parentVal) && !string.IsNullOrWhiteSpace(parentVal))
                        {
                            var parentOpt = parentAttr.Options.FirstOrDefault(o =>
                                string.Equals(o.ValueKey, parentVal, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(o.Label, parentVal, StringComparison.OrdinalIgnoreCase));
                            if (parentOpt is not null)
                                parentOptId = parentOpt.Id;
                            else if (newOptionMap.TryGetValue((parentAttr.Id, parentVal), out var newParentId))
                                parentOptId = newParentId;
                        }
                    }
                }

                var maxSort = attr.Options.Count > 0
                    ? attr.Options.Max(o => o.SortOrder)
                    : 0;

                var option = new CategoryAttributeOption
                {
                    Id = Guid.NewGuid(),
                    CategoryAttributeId = attr.Id,
                    ValueKey = value,
                    Label = value,
                    SortOrder = maxSort + 1,
                    ParentOptionId = parentOptId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.CategoryAttributeOptions.Add(option);
                attr.Options.Add(option);
                newOptionMap[(attr.Id, value)] = option.Id;
                added++;
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Kategori {CatId} için {Count} yeni option değeri kaydedildi.", leafCategoryId, added);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Option değerleri kaydedilirken hata; ana akış etkilenmez.");
        }
    }
}
