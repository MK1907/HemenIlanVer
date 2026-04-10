using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Application.Exceptions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICategoryEnrichmentQueue _enrichmentQueue;

    public AiListingExtractionService(
        AppDbContext db,
        IAiCategoryBootstrapService categoryBootstrap,
        IHttpClientFactory httpFactory,
        IOptions<OpenAiOptions> openAi,
        ILogger<AiListingExtractionService> logger,
        IServiceScopeFactory scopeFactory,
        ICategoryEnrichmentQueue enrichmentQueue)
    {
        _db = db;
        _categoryBootstrap = categoryBootstrap;
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _enrichmentQueue = enrichmentQueue;
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

        // Tüm düzeyler (root → mid → leaf) için 3-katman ağacı
        var rootList = roots.Select(r =>
        {
            var mids = children.Where(c => c.ParentId == r.Id).ToList();
            return new
            {
                r.Name,
                r.Slug,
                children = mids.Select(m => new
                {
                    m.Name,
                    m.Slug,
                    children = children
                        .Where(leaf => leaf.ParentId == m.Id)
                        .Select(leaf => new { leaf.Name, leaf.Slug })
                        .ToList()
                }).ToList()
            };
        }).ToList();

        // Düz liste: leaf kategoriler (alt kategorisi olmayan her kategori)
        var leafCategories = children
            .Where(c => !children.Any(x => x.ParentId == c.Id))
            .Select(c =>
            {
                var parent = children.FirstOrDefault(p => p.Id == c.ParentId);
                var grandparent = parent is not null ? roots.FirstOrDefault(r => r.Id == parent.ParentId) : null;
                var rootCatForLeaf = grandparent ?? roots.FirstOrDefault(r => r.Id == c.ParentId);
                return new
                {
                    c.Name,
                    c.Slug,
                    parentName = parent?.Name,
                    parentSlug = parent?.Slug,
                    rootSlug = (grandparent ?? rootCatForLeaf)?.Slug
                };
            }).ToList();

        // Orta katman (sadece alt kategorisi olanlar - bunlar seçilemez)
        var childList = children.Select(c => new
        {
            c.Name,
            c.Slug,
            c.ParentId,
            parentSlug = roots.FirstOrDefault(r => r.Id == c.ParentId)?.Slug,
            hasChildren = children.Any(x => x.ParentId == c.Id)
        }).ToList();

        var system =
            "Sen Türkiye ilan sitesinde ARAÇ ve EMLAK ilanı kabul eden bir platform için kategori tespiti yapıyorsun.\n\n" +

            "=== ÖNEMLİ KISITLAMA ===\n" +
            "Bu platform YALNIZCA iki kategoriyi kabul eder:\n" +
            "  1. ARAÇ: otomobil, motosiklet, minivan, ticari araç, karavan, deniz aracı, ATV, arazi aracı, elektrikli araç, SUV, pick-up, klasik araç vb.\n" +
            "  2. EMLAK: konut (daire, villa, müstakil ev, köy evi), iş yeri, arsa, konut projesi, bina, devre mülk, turistik tesis vb.\n\n" +
            "Araç veya Emlak DIŞINDA bir ürün/hizmet ise → isValidListing: false, invalidReason: 'Bu platform şu an yalnızca araç ve emlak ilanlarını kabul etmektedir.'\n\n" +

            "=== ADIM ADIM DÜŞÜN ===\n" +
            "0. Metin geçerli bir ilan mı?\n" +
            "   - Anlamsız/rastgele karakterler → isValidListing: false\n" +
            "   - İlan dışı içerik (küfür, mesaj, test) → isValidListing: false\n" +
            "   - Araç veya Emlak dışı kategori (telefon, giysi, elektronik, eğitim vb.) → isValidListing: false\n" +
            "   - Araç veya Emlak ilanıysa → isValidListing: true\n" +
            "1. Araç mı Emlak mı? Alt kategorisini belirle.\n" +
            "2. Mevcut kategorilerden en uygununu seç. Yoksa bootstrap ile oluştur.\n\n" +

            "=== ÖRNEK EŞLEŞTIRMELER ===\n" +
            "- \"2020 BMW X5\" → Araç > Arazi, SUV & Pickup\n" +
            "- \"2018 Fiat Egea Sedan\" → Araç > Otomobil\n" +
            "- \"Ford Transit ticari\" → Araç > Ticari Araçlar\n" +
            "- \"Honda CB500 motosiklet\" → Araç > Motosiklet\n" +
            "- \"Karavan satılık\" → Araç > Karavan\n" +
            "- \"3+1 daire Kadıköy kiralık\" → Emlak > Konut\n" +
            "- \"Çeşme'de villa satılık\" → Emlak > Konut\n" +
            "- \"500 m² arsa\" → Emlak > Arsa\n" +
            "- \"Ofis kiralık Levent\" → Emlak > İş Yeri\n" +
            "- \"iPhone satılık\" → isValidListing: false (araç/emlak değil)\n" +
            "- \"Nike ayakkabı\" → isValidListing: false (araç/emlak değil)\n\n" +

            "=== MEVCUT KATEGORİLER (3 KATMANLI AĞAÇ) ===\n" +
            JsonSerializer.Serialize(rootList) + "\n\n" +
            "SEÇİLEBİLİR YAPRAK KATEGORİLER (alt kategorisi olmayan): " + JsonSerializer.Serialize(leafCategories) + "\n\n" +

            "=== ÇIKTI FORMATI (SADECE JSON) ===\n" +
            "{\"isValidListing\":true/false, \"invalidReason\":\"geçersizse kısa Türkçe açıklama (geçerliyse null)\", " +
            "\"reasoning\":\"kısa düşünce\", \"rootSlug\":\"...\", " +
            "\"suggestedParentSlug\":\"...\"|null, " +
            "\"suggestedChildSlug\":\"...\"|null, \"confidence\":0.0-1.0, " +
            "\"bootstrap\":{\"needed\":true/false, \"rootName\":\"...\", \"rootSlug\":\"...\", " +
            "\"parentName\":\"...\"|null, \"parentSlug\":\"...\"|null, " +
            "\"childName\":\"...\", \"childSlug\":\"...\", " +
            "\"filters\":[{\"key\":\"...\", \"displayName\":\"...\", \"dataType\":\"String|Int|Decimal|Bool|Enum|Money\", \"required\":true/false, " +
            "\"parentKey\":null|\"başka bir filtre key'i (bağımlılık varsa)\", " +
            "\"options\":[{\"valueKey\":\"...\",\"label\":\"...\",\"parentValue\":null|\"parent option valueKey\"}]}]}}\n\n" +

            "=== BOOTSTRAP KURALLARI ===\n" +
            "- Mevcut YAPRAK kategori uygunsa → bootstrap.needed=false, rootSlug + suggestedParentSlug + suggestedChildSlug seç.\n" +
            "- Uygun yaprak kategori YOKSA → bootstrap.needed=true. 3 katman için parentName/parentSlug doldur, " +
            "  2 katman (yeni üst kategori de yeni) için parentName/parentSlug null bırak.\n" +
            "- TÜRKÇE isimler kullan, slug'lar küçük harf ve tire.\n" +
            "- suggestedParentSlug: orta katman slug'ı (Otomobil için 'otomobil', Konut için 'konut' vb.).\n" +
            "- suggestedChildSlug: yaprak katman slug'ı (SUV için 'suv', Daire için 'daire' vb.).\n\n" +

            "=== FİLTRE (ATTRIBUTE) KURALLARI ===\n" +
            "Yeni kategori oluştururken, o ürün/hizmet türünde endüstrideki GERÇEK İLAN SİTELERİNDE (sahibinden.com, letgo, hepsiburada, trendyol, n11, gittigidiyor, arabam.com, emlakjet, hepsiemlak) kullanılan TÜM ÖZELLİKLERİ filters dizisine ekle.\n" +
            "HİÇBİR ÖZELLİĞİ ATLAMA. Kullanıcının ilan girerken dolduracağı her alanı düşün.\n" +
            "En az 15, en fazla 25 filtre üret. Öncelik sırası: tanımlayıcı özellikler (marka, model) > teknik özellikler > durum > fiyat/ticaret bilgisi > diğer.\n" +
            "Enum için options en az 5-8 seçenek koy (gerçekçi, piyasada yaygın olanlar).\n" +
            "Her Enum option'ında gerçek piyasa değerleri kullan (marka için gerçek markalar, renk için gerçek renkler vb.).\n\n" +

            "=== BAĞIMLILIK (PARENT-CHILD) KURALLARI ===\n" +
            "Bazı özellikler başka bir özelliğe BAĞIMLIDIR. Örneğin 'model' → 'marka'ya bağlıdır, 'seri' → 'model'e bağlıdır.\n" +
            "Bağımlı bir filtre tanımlarken parentKey alanına bağlı olduğu filtrenin key'ini yaz.\n" +
            "Bağımlı filtrenin her option'ında parentValue alanına, o option'ın hangi parent seçeneğine ait olduğunu yaz.\n" +
            "Her parent seçeneği için en az 3-5 child option ekle.\n" +
            "Örnek: marka→BMW altında model→3 Serisi, 5 Serisi, X5; marka→Mercedes altında model→C Serisi, E Serisi, GLC.\n" +
            "Bağımlılık yoksa parentKey ve parentValue null/yok olsun.\n\n" +

            "=== KATEGORİ BAZLI TAM FİLTRE LİSTESİ (minimum — bunlardan AZ OLMA, daha fazla ekle) ===\n" +
            "Otomobil / Arazi SUV / Ticari Araç (20+): marka(Enum), model(Enum,parent:marka), seri(Enum,parent:model), yil(Int), km(Int), vitesTipi(Enum:Manuel/Otomatik/Yarı Otomatik/Triptonik), yakitTipi(Enum:Benzin/Dizel/LPG/Hybrid/Elektrik/Benzin & LPG), kasaTipi(Enum:Sedan/Hatchback/SUV/Station Wagon/Coupe/Cabrio/Minivan/Pick-up), motorHacmi(Enum:1.0/1.2/1.4/1.6/1.8/2.0/2.5/3.0+), beygirGucu(Int), renk(Enum:Beyaz/Siyah/Gri/Kırmızı/Mavi/Lacivert/Gümüş/Füme/Kahverengi/Yeşil/Sarı/Turuncu), cekisTipi(Enum:Önden/Arkadan/4x4/AWD), plakaUyrugu(Enum:TR/Yabancı), hasarKaydi(Enum:Hasarsız/Hafif Hasarlı/Ağır Hasarlı/Boyalı/Değişenli), tramerDegeri(Int), garanti(Bool), takasUygun(Bool), kimden(Enum:Sahibinden/Galeriden), durumu(Enum:Sıfır/İkinci El)\n" +
            "Motosiklet (15+): marka(Enum:Honda/Yamaha/Kawasaki/Suzuki/BMW/Ducati/Harley-Davidson/Royal Enfield/KTM/Triumph), model(Enum,parent:marka), yil(Int), km(Int), motor(Enum:125cc/250cc/300cc/400cc/500cc/600cc/650cc/750cc/900cc/1000cc+), tip(Enum:Naked/Sport/Touring/Enduro/Cross/Scooter/Chopper/Cafe Racer), renk(Enum:Siyah/Beyaz/Kırmızı/Mavi/Turuncu/Yeşil/Gri/Sarı), vites(Enum:Manuel/Otomatik/Semi-Otomatik), durumu(Enum:Sıfır/İkinci El), hasarKaydi(Enum:Hasarsız/Hasarlı), ehliyet(Enum:A1/A2/A), kimden(Enum:Sahibinden/Bayiden), takasUygun(Bool), garanti(Bool)\n" +
            "Konut (20+): konutTipi(Enum:Daire/Müstakil/Villa/Residence/Yazlık/Çatı Katı/Dublex/Triplex), odaSayisi(Enum:1+0/1+1/2+1/3+1/4+1/5+1/6+), m2Brut(Int), m2Net(Int), binaYasi(Int), bulunduguKat(Enum:Giriş/1/2/3/4/5/6/7-10/11-15/16-20/Çatı), toplamKat(Int), isitmaTipi(Enum:Doğalgaz Kombi/Merkezi/Soba/Klima/Yerden Isıtma/Isı Pompası), banyoSayisi(Int), balkon(Bool), esyali(Bool), siteIcinde(Bool), otopark(Enum:Açık/Kapalı/Yok), cephe(Enum:Kuzey/Güney/Doğu/Batı/Güneydoğu/Güneybatı), yapininDurumu(Enum:Sıfır/İkinci El/Devam Eden Proje), tapuDurumu(Enum:Kat Mülkiyetli/Kat İrtifaklı/Hisseli/Müstakil/Kooperatif), aidat(Int), kimden(Enum:Sahibinden/Emlakçıdan/İnşaat Firmasından), krediyeUygun(Bool), takas(Bool)\n" +
            "Arsa (12+): arsaTipi(Enum:İmarlı/İmarsız/Tarla/Bağ & Bahçe/Sanayi/Ticari), m2(Int), ada(String), parsel(String), imar(Enum:Konut/Ticaret/Turizm/Sanayi/Tarım/Karma), taks(Decimal), kaks(Decimal), tapuDurumu(Enum:Hisseli/Müstakil/Arazi), altyapi(Enum:Var/Yok/Kısmen), yol(Enum:Asfalt/Stabilize/Toprak), konum(String), kimden(Enum:Sahibinden/Emlakçıdan), krediyeUygun(Bool), takas(Bool)\n" +
            "İş Yeri (15+): isyeriTipi(Enum:Ofis/Dükkan/Depo/Fabrika/Atölye/Plaza/AVM/Restoran/Cafe/Otel), m2Brut(Int), m2Net(Int), binaYasi(Int), bulunduguKat(Enum:Bodrum/Giriş/1/2/3/4/5+), isitmaTipi(Enum:Doğalgaz Kombi/Merkezi/Klima/Soba/Yerden Isıtma), siteIcinde(Bool), esyali(Bool), vitrin(Bool), wc(Bool), depo(Bool), aidat(Int), tapuDurumu(Enum:Kat Mülkiyetli/Kat İrtifaklı/Hisseli/Müstakil), kimden(Enum:Sahibinden/Emlakçıdan), krediyeUygun(Bool)";

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

        // Geçersiz ilan kontrolü
        if (doc.TryGetProperty("isValidListing", out var validProp) && validProp.ValueKind == JsonValueKind.False)
        {
            var reason = doc.TryGetProperty("invalidReason", out var rp) && rp.ValueKind == JsonValueKind.String
                ? rp.GetString()
                : "Girilen metin bir ilan tanımlamıyor.";
            throw new InvalidListingPromptException(reason!);
        }

        var hasBootstrap = doc.TryGetProperty("bootstrap", out var bCheck);
        var bootNeeded = hasBootstrap && bCheck.TryGetProperty("needed", out var nCheck) ? nCheck.GetRawText() : "N/A";
        var bootFilterCount = hasBootstrap && bCheck.TryGetProperty("filters", out var fCheck) && fCheck.ValueKind == JsonValueKind.Array ? fCheck.GetArrayLength() : 0;
        _logger.LogInformation("AI response: hasBootstrap={Has}, needed={Needed}, filterCount={FC}, rootSlug={RS}",
            hasBootstrap, bootNeeded, bootFilterCount,
            doc.TryGetProperty("rootSlug", out var rsLog) ? rsLog.GetRawText() : "N/A");

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

        var rootSlugRaw = doc.TryGetProperty("rootSlug", out var rs) ? rs.GetString() : null;
        var parentSlugRaw = doc.TryGetProperty("suggestedParentSlug", out var ps) && ps.ValueKind == JsonValueKind.String ? ps.GetString() : null;
        var childSlugRaw = doc.TryGetProperty("suggestedChildSlug", out var cs) ? cs.GetString() : null;
        var conf = doc.TryGetProperty("confidence", out var cf) && cf.TryGetDouble(out var cfd) ? cfd : 0.75;

        if (string.IsNullOrWhiteSpace(rootSlugRaw))
            throw new InvalidOperationException("OpenAI yanıtında rootSlug yok veya geçersiz.");

        var rootSlugNorm = CategorySlugHelper.SanitizeSlug(rootSlugRaw);
        var parentSlugNorm = !string.IsNullOrWhiteSpace(parentSlugRaw) ? CategorySlugHelper.SanitizeSlug(parentSlugRaw) : null;
        var childSlugNorm = !string.IsNullOrWhiteSpace(childSlugRaw) ? CategorySlugHelper.SanitizeSlug(childSlugRaw) : null;

        var rootCat = roots.FirstOrDefault(r => r.Slug == rootSlugNorm)
            ?? roots.FirstOrDefault(r => CategorySlugHelper.SlugEquals(r.Slug, rootSlugRaw))
            ?? roots.FirstOrDefault(r => r.Slug == CategorySlugHelper.NormalizeToAscii(rootSlugRaw));

        if (rootCat is null)
        {
            _logger.LogWarning("rootSlug '{Slug}' (normalized: '{Norm}') DB'de bulunamadı, force bootstrap çalıştırılıyor.", rootSlugRaw, rootSlugNorm);

            string filtersJson = "[]";
            if (doc.TryGetProperty("bootstrap", out var origBoot) && origBoot.TryGetProperty("filters", out var origF) && origF.ValueKind == JsonValueKind.Array)
                filtersJson = origF.GetRawText();

            var rootNameStr = doc.TryGetProperty("bootstrap", out var bn1) && bn1.TryGetProperty("rootName", out var rn1) && rn1.ValueKind == JsonValueKind.String ? rn1.GetString()! : rootSlugRaw!;
            var childNameStr = doc.TryGetProperty("bootstrap", out var bn2) && bn2.TryGetProperty("childName", out var cn1) && cn1.ValueKind == JsonValueKind.String ? cn1.GetString()! : (!string.IsNullOrEmpty(childSlugRaw) ? childSlugRaw : "Genel");
            var childSlugStr = doc.TryGetProperty("bootstrap", out var bn3) && bn3.TryGetProperty("childSlug", out var cs1) && cs1.ValueKind == JsonValueKind.String ? cs1.GetString()! : (!string.IsNullOrEmpty(childSlugRaw) ? childSlugRaw : "genel");

            var forceJson = "{\"bootstrap\":{\"needed\":true,"
                + "\"rootName\":" + JsonSerializer.Serialize(rootNameStr) + ","
                + "\"rootSlug\":" + JsonSerializer.Serialize(rootSlugRaw!) + ","
                + "\"childName\":" + JsonSerializer.Serialize(childNameStr) + ","
                + "\"childSlug\":" + JsonSerializer.Serialize(childSlugStr) + ","
                + "\"filters\":" + filtersJson + "}}";
            _logger.LogInformation("Force bootstrap JSON (filters count): {Count}", filtersJson == "[]" ? 0 : filtersJson.Length);
            var forceDoc = JsonDocument.Parse(forceJson).RootElement;
            await _categoryBootstrap.ApplyFromDetectDocumentAsync(forceDoc, ct);

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

            rootSlugNorm = CategorySlugHelper.SanitizeSlug(rootSlugRaw);
            rootCat = roots.FirstOrDefault(r => r.Slug == rootSlugNorm)
                ?? roots.FirstOrDefault(r => CategorySlugHelper.SlugEquals(r.Slug, rootSlugRaw))
                ?? roots.LastOrDefault();

            if (rootCat is null)
                throw new InvalidOperationException($"Kategori oluşturulamadı (rootSlug: {rootSlugRaw}).");
        }

        // Orta katman kategoriler (root'un direkt çocukları)
        var midCats = children.Where(c => c.ParentId == rootCat.Id).ToList();

        // subs: kullanıcıya gösterilen alt kategori seçenekleri
        // Önce grandchild'ları (parent slug altındakiler) göster, yoksa root'un direkt çocukları
        IReadOnlyList<SubCategoryOptionDto> subs;
        if (!string.IsNullOrEmpty(parentSlugNorm))
        {
            var midCat = midCats.FirstOrDefault(m => m.Slug == parentSlugNorm)
                ?? midCats.FirstOrDefault(m => CategorySlugHelper.SlugEquals(m.Slug, parentSlugRaw!));
            var grandchildren = midCat is not null
                ? children.Where(c => c.ParentId == midCat.Id).ToList()
                : [];
            subs = (grandchildren.Count > 0 ? grandchildren : midCats)
                .Select(c => new SubCategoryOptionDto(c.Id, c.Name, c.Slug))
                .ToList();
        }
        else
        {
            subs = midCats.Select(c => new SubCategoryOptionDto(c.Id, c.Name, c.Slug)).ToList();
        }

        Guid? leafId = null;
        if (!string.IsNullOrEmpty(childSlugRaw))
        {
            // 3-katman: önce parentSlug altındaki grandchild'ı ara
            if (!string.IsNullOrEmpty(parentSlugNorm))
            {
                var midCat = midCats.FirstOrDefault(m => m.Slug == parentSlugNorm)
                    ?? midCats.FirstOrDefault(m => CategorySlugHelper.SlugEquals(m.Slug, parentSlugRaw!));
                if (midCat is not null)
                {
                    var grandchildMatch = children.FirstOrDefault(c => c.ParentId == midCat.Id && c.Slug == childSlugNorm)
                        ?? children.FirstOrDefault(c => c.ParentId == midCat.Id && CategorySlugHelper.SlugEquals(c.Slug, childSlugRaw));
                    if (grandchildMatch is not null) leafId = grandchildMatch.Id;
                }
            }

            // 2-katman fallback: root'un direkt çocuğu
            if (leafId is null)
            {
                var match = children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == childSlugNorm)
                    ?? children.FirstOrDefault(c => c.ParentId == rootCat.Id && CategorySlugHelper.SlugEquals(c.Slug, childSlugRaw))
                    ?? children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == CategorySlugHelper.NormalizeToAscii(childSlugRaw));
                if (match is not null) leafId = match.Id;
                else leafId = children.FirstOrDefault(c => c.ParentId == rootCat.Id)?.Id;
            }
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

                // Kategoriyi AI çıktısıyla kuyruğa ekle — worker önce tespit edilen değerleri işler
                _enrichmentQueue.Enqueue(new Application.Abstractions.CategoryEnrichmentJob(
                    leafId.Value,
                    sugAttrs));

                // Arka planda kaydet — kullanıcıyı beklettirmez
                if (sugAttrs is { Count: > 0 })
                {
                    var capturedLeafId = leafId.Value;
                    var capturedAttrs = sugAttrs;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var scope = _scopeFactory.CreateAsyncScope();
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var svc = new AiOptionPersister(db, _logger);
                            await svc.PersistAsync(capturedLeafId, capturedAttrs, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Arka plan option kaydetme başarısız.");
                        }
                    });
                }
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

        // Kod seviyesinde Enum doğrulama için options map
        var attrOptionMap = attrs
            .Where(a => a.DataType == AttributeDataType.Enum || a.Options.Count > 0)
            .ToDictionary(
                a => a.AttributeKey,
                a => a.Options.Select(o => o.ValueKey).ToHashSet(StringComparer.OrdinalIgnoreCase)
            );

        var attrSpec = attrs.Select(a => new
        {
            key = a.AttributeKey,
            displayName = a.DisplayName,
            dataType = a.DataType.ToString(),
            required = a.IsRequired,
            options = a.Options.OrderBy(o => o.SortOrder).Select(o => new { o.ValueKey, o.Label }).ToList()
        }).ToList();

        // Tek AI çağrısında çıkar + doğrula (options listesi kılavuz, zorunlu değil)
        var system =
            "Sen Türkiye ilan sitelerinde ilan metni ayrıştırıcı ve veri kalite uzmanısın. Yanıtını JSON formatında ver.\n\n" +
            "ALANLAR: " + JsonSerializer.Serialize(attrSpec) + "\n\n" +
            "KURALLAR:\n" +
            "1. Enum alanlar (options listesi varsa) → önce listeden valueKey ile seç. " +
               "Listede yoksa ama gerçek ve bilinen bir sektör değeriyse yine de ekle.\n" +
            "2. Üretici/marka alanı için: metinde geçen ifade gerçek bir üretici firma değilse (paket adı, slogan, donanım kodu vb.) O ALANI EKLEME.\n" +
            "3. String: serbest metin. Int/Decimal/Money: sadece sayı. Bool: true/false.\n" +
            "4. Metinden çıkaramadığın veya emin olmadığın alanları EKLEME.\n\n" +
            "JSON ÇIKTI FORMATI: {\"suggestedTitle\":\"...\",\"suggestedDescription\":\"...\",\"suggestedPrice\":null,\"attributes\":{\"key\":\"value\",...}}\n" +
            "- suggestedTitle: Türkçe, kısa ve çekici (maks 100 karakter)\n" +
            "- suggestedDescription: 2-3 cümle\n" +
            "- suggestedPrice: TRY tahmini, bilinmiyorsa null\n" +
            "- attributes: SADECE gerçek, bilinen sektör değerleri — kategori ne olursa olsun geçerli kural";

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

        var rawValues = new Dictionary<string, string>();
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
                if (val is null) continue;

                // Enum alanı: listede yoksa AI'ya güven (prompt zaten sahte değerleri filtreler),
                // değeri kabul et — arka planda DB'ye kaydedilecek.
                if (attrOptionMap.TryGetValue(prop.Name, out var validOptions)
                    && validOptions.Count > 0
                    && !validOptions.Contains(val))
                {
                    _logger.LogInformation(
                        "Enum '{Key}'='{Val}' options listesinde yok; AI'ya güvenilerek kabul edildi.",
                        prop.Name, val);
                }

                rawValues[prop.Name] = val;
            }
        }

        return (title, desc, price, rawValues.Count > 0 ? rawValues : null);
    }

}

/// <summary>Yeni option değerlerini arka planda kaydetmek için bağımsız scope'ta çalışan yardımcı.</summary>
internal sealed class AiOptionPersister(AppDbContext db, ILogger logger)
{
    public async Task PersistAsync(Guid leafCategoryId, IReadOnlyDictionary<string, string> extractedValues, CancellationToken ct)
    {
        try
        {
            var attrs = await db.CategoryAttributes
                .Include(x => x.Options)
                .Where(x => x.CategoryId == leafCategoryId)
                .ToListAsync(ct);

            var added = 0;
            var newOptionMap = new Dictionary<(Guid attrId, string value), Guid>();

            foreach (var (key, value) in extractedValues)
            {
                if (string.IsNullOrWhiteSpace(value) || value is "true" or "false") continue;

                var attr = attrs.FirstOrDefault(a => string.Equals(a.AttributeKey, key, StringComparison.OrdinalIgnoreCase));
                if (attr is null) continue;
                if (attr.DataType is AttributeDataType.Bool or AttributeDataType.Money
                    or AttributeDataType.Decimal or AttributeDataType.Int) continue;

                var existing = attr.Options.FirstOrDefault(o =>
                    string.Equals(o.ValueKey, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(o.Label, value, StringComparison.OrdinalIgnoreCase));
                if (existing is not null) { newOptionMap[(attr.Id, value)] = existing.Id; continue; }

                Guid? parentOptId = null;
                if (attr.ParentAttributeId.HasValue)
                {
                    var parentAttr = attrs.FirstOrDefault(a => a.Id == attr.ParentAttributeId.Value);
                    if (parentAttr is not null && extractedValues.TryGetValue(parentAttr.AttributeKey, out var parentVal) && !string.IsNullOrWhiteSpace(parentVal))
                    {
                        var parentOpt = parentAttr.Options.FirstOrDefault(o =>
                            string.Equals(o.ValueKey, parentVal, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(o.Label, parentVal, StringComparison.OrdinalIgnoreCase));
                        if (parentOpt is not null) parentOptId = parentOpt.Id;
                        else if (newOptionMap.TryGetValue((parentAttr.Id, parentVal), out var npId)) parentOptId = npId;
                    }
                }

                var maxSort = attr.Options.Count > 0 ? attr.Options.Max(o => o.SortOrder) : 0;
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
                db.CategoryAttributeOptions.Add(option);
                attr.Options.Add(option);
                newOptionMap[(attr.Id, value)] = option.Id;
                added++;
            }

            if (added > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Arka plan: kategori {CatId} için {Count} yeni option kaydedildi.", leafCategoryId, added);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Arka plan option kaydetme hatası.");
        }
    }
}
