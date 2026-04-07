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
            "0. ÖNCE metnin gerçek bir ilan olup olmadığını değerlendir:\n" +
            "   - Anlamsız/rastgele karakterler (örn: 'asdfgh', 'xyzxyz', '123abc', 'aaaaaa') → isValidListing: false\n" +
            "   - İlan dışı içerik (küfür, kişisel mesaj, test metni, sadece sayı/sembol) → isValidListing: false\n" +
            "   - Gerçek bir ürün/hizmet/mülk tanımıyorsa → isValidListing: false\n" +
            "   - Gerçek bir ilan olabilecek metinse → isValidListing: true\n" +
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
            "{\"isValidListing\":true/false, \"invalidReason\":\"geçersizse kısa Türkçe açıklama (geçerliyse null)\", " +
            "\"reasoning\":\"kısa düşünce (ürün nedir, hangi kategoriye ait)\", \"rootSlug\":\"...\", \"suggestedChildSlug\":\"...\"|null, \"confidence\":0.0-1.0, " +
            "\"bootstrap\":{\"needed\":true/false, \"rootName\":\"...\", \"rootSlug\":\"...\", \"childName\":\"...\", \"childSlug\":\"...\", " +
            "\"filters\":[{\"key\":\"...\", \"displayName\":\"...\", \"dataType\":\"String|Int|Decimal|Bool|Enum|Money\", \"required\":true/false, " +
            "\"parentKey\":null|\"başka bir filtre key'i (bağımlılık varsa)\", " +
            "\"options\":[{\"valueKey\":\"...\",\"label\":\"...\",\"parentValue\":null|\"parent option valueKey\"}]}]}}\n\n" +

            "=== BOOTSTRAP KURALLARI ===\n" +
            "- Mevcut slug'lardan biri uygunsa → bootstrap.needed=false, o slug'ı seç.\n" +
            "- Uygun kategori YOKSA → bootstrap.needed=true, doğru isim ve slug ile yeni oluştur.\n" +
            "- TÜRKÇE isimler kullan, slug'lar küçük harf ve tire.\n\n" +

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
            "Otomobil (20+): marka(Enum), model(Enum,parent:marka), seri(Enum,parent:model), yıl(Int), km(Int), vitesTipi(Enum:Manuel/Otomatik/Yarı Otomatik/Triptonik), yakitTipi(Enum:Benzin/Dizel/LPG/Hybrid/Elektrik/Benzin & LPG), kasaTipi(Enum:Sedan/Hatchback/SUV/Station Wagon/Coupe/Cabrio/Minivan/Pick-up), motorHacmi(Enum:1.0/1.2/1.4/1.6/1.8/2.0/2.5/3.0+), beygirGucu(Int), renk(Enum:Beyaz/Siyah/Gri/Kırmızı/Mavi/Lacivert/Gümüş/Füme/Kahverengi/Yeşil/Sarı/Turuncu), cekisTipi(Enum:Önden/Arkadan/4x4/AWD), plakaUyrugu(Enum:TR/Yabancı), hasarKaydi(Enum:Hasarsız/Hafif Hasarlı/Ağır Hasarlı/Boyalı/Değişenli), tramerdegeri(Int), garanti(Bool), takasUygun(Bool), kimden(Enum:Sahibinden/Galeriden), durumu(Enum:Sıfır/İkinci El)\n" +
            "Konut (20+): ilanTipi(Enum:Satılık/Kiralık), konutTipi(Enum:Daire/Müstakil/Villa/Residence/Yazlık/Çatı Katı/Dublex/Triplex), odaSayisi(Enum:1+0/1+1/2+1/3+1/4+1/5+1/6+), m2Brut(Int), m2Net(Int), binaYasi(Int), bulunduguKat(Enum:Giriş/1/2/3/4/5/6/7-10/11-15/16-20/Çatı), toplamKat(Int), isitmaTipi(Enum:Doğalgaz Kombi/Merkezi/Soba/Klima/Yerden Isıtma/Isı Pompası), banyoSayisi(Int), balkon(Bool), esyali(Bool), siteIcinde(Bool), otopark(Enum:Açık/Kapalı/Yok), cephe(Enum:Kuzey/Güney/Doğu/Batı/Güneydoğu/Güneybatı), yapininDurumu(Enum:Sıfır/İkinci El/Devam Eden Proje), tapuDurumu(Enum:Kat Mülkiyetli/Kat İrtifaklı/Hisseli/Müstakil/Kooperatif), aidat(Int), kimden(Enum:Sahibinden/Emlakçıdan/İnşaat Firmasından), krediyeUygun(Bool), takas(Bool)\n" +
            "Cep Telefonu (18+): marka(Enum:Apple/Samsung/Xiaomi/Huawei/Oppo/Vivo/Realme/OnePlus/Google/Nothing), model(Enum,parent:marka), dahiliHafiza(Enum:32GB/64GB/128GB/256GB/512GB/1TB), ram(Enum:2GB/3GB/4GB/6GB/8GB/12GB/16GB), ekranBoyutu(Enum:5.0/5.5/6.0/6.1/6.4/6.5/6.7/6.8/6.9), renk(Enum:Siyah/Beyaz/Mavi/Kırmızı/Yeşil/Mor/Sarı/Gri/Altın/Gümüş), bataryaKapasitesi(Int), isletimSistemi(Enum:iOS/Android/HarmonyOS), kameraMegapiksel(Int), garanti(Bool), durumu(Enum:Sıfır/İkinci El/Yenilenmiş), kutusuVarMi(Bool), faturaliMi(Bool), agDesteği(Enum:4G/5G), ciftHat(Bool), ekranTipi(Enum:AMOLED/OLED/LCD/IPS), kimden(Enum:Sahibinden/Mağazadan), takas(Bool)\n" +
            "Çanta & Aksesuar (16+): marka(Enum:Louis Vuitton/Gucci/Prada/Chanel/Hermès/Michael Kors/Coach/Zara/Mango/Vakko/Beymen), tip(Enum:El Çantası/Omuz Çantası/Sırt Çantası/Clutch/Bel Çantası/Evrak Çantası/Valiz/Cüzdan), malzeme(Enum:Deri/Sentetik Deri/Kumaş/Kanvas/Naylon/Süet), renk(Enum:Siyah/Beyaz/Kahverengi/Taba/Kırmızı/Lacivert/Bej/Pembe/Gri/Yeşil), boyut(Enum:Mini/Küçük/Orta/Büyük/Ekstra Büyük), cinsiyet(Enum:Kadın/Erkek/Unisex), durum(Enum:Sıfır/Az Kullanılmış/Kullanılmış), orijinallik(Enum:Orijinal/A Kalite/Replika), kutuSertifika(Bool), seriNumarasi(Bool), garantiDurumu(Bool), uretimYili(Int), koleksiyonSeri(String), kimden(Enum:Sahibinden/Mağazadan/Komisyoncudan), takas(Bool), fiyatPazarlik(Bool)\n" +
            "Ayakkabı (16+): marka(Enum:Nike/Adidas/Puma/New Balance/Converse/Vans/Skechers/Reebok/Asics/Salomon/Timberland), model(Enum,parent:marka), numara(Enum:36/37/38/39/40/41/42/43/44/45/46), renk(Enum:Siyah/Beyaz/Kırmızı/Mavi/Gri/Kahverengi/Yeşil/Turuncu/Pembe/Çok Renkli), cinsiyet(Enum:Kadın/Erkek/Unisex/Çocuk), tip(Enum:Spor/Günlük/Klasik/Bot/Çizme/Sandalet/Terlik/Koşu/Outdoor/Krampon), malzeme(Enum:Deri/Sentetik/Kanvas/Tekstil/Süet), taban(Enum:Kauçuk/EVA/Köpük/Deri), durum(Enum:Sıfır/Az Kullanılmış/Kullanılmış), kutuVarMi(Bool), orijinallik(Enum:Orijinal/A Kalite/Replika), sezon(Enum:İlkbahar-Yaz/Sonbahar-Kış/4 Mevsim), kimden(Enum:Sahibinden/Mağazadan), takas(Bool), topukYuksekligi(Enum:Düz/Alçak/Orta/Yüksek), fiyatPazarlik(Bool)\n" +
            "Bilgisayar / Laptop (18+): marka(Enum:Apple/Lenovo/Asus/HP/Dell/MSI/Acer/Monster/Huawei/Microsoft), model(Enum,parent:marka), islemci(Enum:Intel i3/Intel i5/Intel i7/Intel i9/AMD Ryzen 3/AMD Ryzen 5/AMD Ryzen 7/AMD Ryzen 9/Apple M1/Apple M2/Apple M3/Apple M4), ram(Enum:4GB/8GB/16GB/32GB/64GB), depolamaKapasitesi(Enum:128GB SSD/256GB SSD/512GB SSD/1TB SSD/2TB SSD/1TB HDD/2TB HDD), ekranBoyutu(Enum:13.3/14/15.6/16/17.3), ekranCozunurlugu(Enum:HD/FHD/2K/4K), ekranKarti(Enum:Dahili/NVIDIA RTX 3050/RTX 3060/RTX 4050/RTX 4060/RTX 4070/RTX 4080/RTX 4090/AMD Radeon), isletimSistemi(Enum:Windows 11/Windows 10/macOS/Linux/FreeDOS), renk(Enum:Gri/Siyah/Gümüş/Beyaz/Mavi), bataryaOmru(Int), agirlik(Decimal), garanti(Bool), durumu(Enum:Sıfır/İkinci El/Yenilenmiş), kutusuVarMi(Bool), kimden(Enum:Sahibinden/Mağazadan), takas(Bool)\n" +
            "Mobilya (14+): tip(Enum:Koltuk Takımı/Yatak Odası/Yemek Masası/TV Ünitesi/Kitaplık/Gardırop/Sehpa/Çalışma Masası/Sandalye/Büfe/Konsol), marka(Enum:İstikbal/Bellona/Doğtaş/Kelebek/Çilek/Mondi/Yataş/IKEA/Mudo/Diğer), malzeme(Enum:Ahşap/MDF/Kumaş/Deri/Metal/Cam/Mermer), renk(Enum:Beyaz/Siyah/Kahverengi/Ceviz/Gri/Krem/Bej/Antrasit), durum(Enum:Sıfır/Az Kullanılmış/Kullanılmış/Yenilenmiş), adet(Int), boyutlar(String), stil(Enum:Modern/Klasik/Rustik/Minimalist/Retro/Bohem/Scandinavian), kimden(Enum:Sahibinden/Mağazadan), garanti(Bool), montajDahil(Bool), takas(Bool), teslimatSekli(Enum:Alıcı Öder/Satıcı Karşılar/Elden Teslim), fiyatPazarlik(Bool)\n" +
            "Özel Ders / Eğitim (12+): brans(Enum:Matematik/Fizik/Kimya/Biyoloji/Türkçe/İngilizce/Almanca/Fransızca/Tarih/Coğrafya/Müzik/Resim/Yazılım), hedefSinav(Enum:LGS/YKS-TYT/YKS-AYT/KPSS/ALES/YDS/DGS/Sınıf İçi/Yok), formatı(Enum:Yüz Yüze/Online/Hibrit), seviye(Enum:İlkokul/Ortaokul/Lise/Üniversite/Yetişkin), tecrube(Enum:0-2 Yıl/3-5 Yıl/6-10 Yıl/10+ Yıl), konum(String), dersSuresi(Enum:30dk/45dk/60dk/90dk/120dk), grupMu(Enum:Bireysel/Grup/Her İkisi), kimden(Enum:Öğretmen/Üniversite Öğrencisi/Eğitim Kurumu), referansVarMi(Bool), uygunluk(Enum:Hafta İçi/Hafta Sonu/Her Gün/Akşam), ilkDersUcretsiz(Bool)";

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
        var childSlugRaw = doc.TryGetProperty("suggestedChildSlug", out var cs) ? cs.GetString() : null;
        var conf = doc.TryGetProperty("confidence", out var cf) && cf.TryGetDouble(out var cfd) ? cfd : 0.75;

        if (string.IsNullOrWhiteSpace(rootSlugRaw))
            throw new InvalidOperationException("OpenAI yanıtında rootSlug yok veya geçersiz.");

        var rootSlugNorm = CategorySlugHelper.SanitizeSlug(rootSlugRaw);
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

        var subs = children.Where(c => c.ParentId == rootCat.Id)
            .Select(c => new SubCategoryOptionDto(c.Id, c.Name, c.Slug))
            .ToList();

        Guid? leafId = null;
        if (!string.IsNullOrEmpty(childSlugRaw))
        {
            var match = children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == childSlugNorm)
                ?? children.FirstOrDefault(c => c.ParentId == rootCat.Id && CategorySlugHelper.SlugEquals(c.Slug, childSlugRaw))
                ?? children.FirstOrDefault(c => c.ParentId == rootCat.Id && c.Slug == CategorySlugHelper.NormalizeToAscii(childSlugRaw));
            if (match is not null) leafId = match.Id;
            else leafId = children.FirstOrDefault(c => c.ParentId == rootCat.Id)?.Id;
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
