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
            .Include(x => x.AttributeValues).ThenInclude(v => v.CategoryAttribute)
            .FirstOrDefaultAsync(x => x.Id == listingId, ct)
            ?? throw new InvalidOperationException("İlan bulunamadı.");

        var imageUrls = listing.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Url)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Take(6)
            .ToList();

        // İlanda beyan edilen marka/model/yıl bilgilerini al
        string? GetAttr(string key) => listing.AttributeValues
            .FirstOrDefault(v => string.Equals(v.CategoryAttribute?.AttributeKey, key, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

        var declaredBrand = GetAttr("brand");
        var declaredModel = GetAttr("model");
        var declaredYear  = GetAttr("year");
        var declaredColor = GetAttr("color");

        if (imageUrls.Count == 0)
            return NoPhotoResult();

        if (!string.IsNullOrWhiteSpace(_openAi.ApiKey))
        {
            try
            {
                return await CallOpenAiVisionAsync(listing.Category.Name, imageUrls,
                    declaredBrand, declaredModel, declaredYear, declaredColor, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI fotoğraf analizi başarısız: {Message}", ex.Message);
            }
        }

        return BuildFallbackAnalysis(imageUrls.Count);
    }

    private async Task<PhotoAnalysisDto> CallOpenAiVisionAsync(
        string categoryName, List<string> imageUrls,
        string? declaredBrand, string? declaredModel, string? declaredYear, string? declaredColor,
        CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        // Beyan edilen bilgileri user mesajına ekle
        var declaredInfo = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(declaredBrand)) declaredInfo.Append($"Beyan edilen Marka: {declaredBrand}\n");
        if (!string.IsNullOrWhiteSpace(declaredModel)) declaredInfo.Append($"Beyan edilen Model: {declaredModel}\n");
        if (!string.IsNullOrWhiteSpace(declaredYear))  declaredInfo.Append($"Beyan edilen Yıl: {declaredYear}\n");
        if (!string.IsNullOrWhiteSpace(declaredColor)) declaredInfo.Append($"Beyan edilen Renk: {declaredColor}\n");

        var system =
            "Sen Türkiye'nin en deneyimli ikinci el araç eksper uzmanısın. " +
            "Otomotiv sektöründe 20 yıllık tecrübeyle araçları fotoğraftan analiz ediyorsun.\n\n" +

            "=== ÇIKTI FORMATI (SADECE JSON, başka hiçbir şey yazma) ===\n" +
            "{\n" +
            "  \"overallConditionScore\": <0-100 tam sayı>,\n" +
            "  \"conditionLabel\": \"Çok İyi\" | \"İyi\" | \"Orta\" | \"Kötü\",\n" +
            "\n" +
            "  \"detectedBrand\": \"<fotoğraftan tespit edilen marka, örn: Ford, BMW, Toyota; tespit edilemezse null>\",\n" +
            "  \"detectedModel\": \"<model adı, örn: Focus, 3 Serisi, Corolla; null>\",\n" +
            "  \"detectedYear\": \"<tahmini model yılı veya aralığı, örn: '2018-2020'; null>\",\n" +
            "  \"detectedColor\": \"<araç rengi Türkçe, örn: Beyaz, Siyah, Gümüş Gri; null>\",\n" +
            "  \"detectedBodyType\": \"<kasa tipi: Sedan | Hatchback | SUV | Crossover | Pickup | Minivan | Coupe | Cabrio | Kombi | Van; null>\",\n" +
            "  \"detectedFuelType\": \"<yakıt: Benzin | Dizel | LPG | Hibrit | Elektrik | Benzin+LPG; null>\",\n" +
            "  \"detectedTransmission\": \"<şanzıman: Manuel | Otomatik | Yarı Otomatik | CVT; null>\",\n" +
            "  \"detectedEngineSize\": \"<motor hacmi tahmini, örn: '1.6 TDCi', '2.0 TFSI'; null>\",\n" +
            "  \"detectedKmApprox\": \"<göstergeden okunan veya tahmini km, örn: '85.000 km'; null>\",\n" +
            "\n" +
            "  \"hasScratchOrDent\": <true|false>,\n" +
            "  \"hasPaintDifference\": <true|false — boya renk farkı veya yeniden boyama izi>,\n" +
            "  \"hasGlassDamage\": <true|false — cam çatlağı, kırık veya çizik>,\n" +
            "  \"hasWheelOrTireDamage\": <true|false — jant ezik/çizik, lastik yıpranma>,\n" +
            "  \"hasRustOrCorrosion\": <true|false — pas, korozyon>,\n" +
            "  \"hasBodyDeformation\": <true|false — kaporta ezik, deformasyon>,\n" +
            "\n" +
            "  \"interiorDamage\": <true|false>,\n" +
            "  \"hasSeatWear\": <true|false — koltuk yırtık/soluk/lekeli>,\n" +
            "  \"hasDashboardDamage\": <true|false — gösterge paneli çatlak/kırık>,\n" +
            "  \"hasCeilingStain\": <true|false — tavan halısı leke/nem>,\n" +
            "\n" +
            "  \"suspectedTaxiOrRental\": <true|false — sarı renk, taksi lambası izi, filoya ait sticker izi, arka koltuk bölücü plastik yuvası>,\n" +
            "  \"suspectedAccidentHistory\": <true|false — farklı boya tonları, işçilik kalitesi düşük onarım, hizasız kaporta parçaları>,\n" +
            "  \"suspectedKmTampering\": <true|false — gösterge km ile araç yaşı/yıpranması uyumsuz>,\n" +
            "  \"hasHiddenAreas\": <true|false — kasıtlı kötü açı, aşırı karanlık/gizlenen bölge>,\n" +
            "\n" +
            "  \"brandMismatch\": <true|false — fotoğraftaki araç markası/modeli beyan edilenden FARKLI ise true>,\n" +
            "  \"brandMismatchDetail\": \"<Fark varsa açıklama, örn: 'Fotoğraf Toyota Corolla gösteriyor, ilan Dodge belirtiyor'; fark yoksa null>\",\n" +
            "\n" +
            "  \"photoQualityScore\": <0-100, fotoğraf kalitesi>,\n" +
            "  \"isProfessionalPhoto\": <true|false>,\n" +
            "\n" +
            "  \"findings\": [\"<somut tespit 1>\", \"<somut tespit 2>\", ...],\n" +
            "  \"warnings\": [\"<ciddi uyarı 1>\", ...],\n" +
            "  \"summary\": \"<3-4 cümle kapsamlı Türkçe ekspertiz özeti>\"\n" +
            "}\n\n" +

            "=== PUANLAMA KILAVUZU ===\n" +
            "90-100: Sıfır veya sıfır gibi, hasar yok, boya orijinal, iç temiz\n" +
            "75-89: Çok iyi, küçük yüzeysel izler, genel bakımlı\n" +
            "55-74: İyi, bazı yıpranmalar veya küçük dokunmalar, kullanılabilir\n" +
            "35-54: Orta, görünür hasar veya onarım izi, değer düşürücü\n" +
            "0-34:  Kötü, ciddi hasar, kaza geçmişi kuvvetle muhtemel\n\n" +

            "=== ÖNEMLİ: MARKA/MODEL DOĞRULAMA ===\n" +
            "İlan sahibi beyan ettiği marka/model ile fotoğraftaki araç MUTLAKA karşılaştırılmalı.\n" +
            "Fotoğraftaki araç farklı bir markaya/modele ait ise brandMismatch=true YAP ve brandMismatchDetail'de\n" +
            "HER İKİ markayı da belirt. Bu en kritik bulgudur, asla atlama.\n\n" +
            "=== EKSPERTİZ KONTROL LİSTESİ ===\n" +
            "DIŞ KAPORTA: Her kapı, çamurluk, tavan, kaput, bagaj kapağı için boya uyumu, panel hizası, kaynak izi\n" +
            "BOYA ANALİZİ: Renk tonu farklılığı, turuncu kabuğu, mat nokta, yeniden boyama belirtileri\n" +
            "CAM & ÇERÇEVE: Ön cam çatlak/taş isabet, yan camlar, ayna hasarı, conta durumu\n" +
            "JANT & LASTİK: Jant ezik/çizik/boya, lastik diş derinliği, marka ve ebat uyumu\n" +
            "IŞIKLAR: Far çatlağı, su/nem içeri girmiş mi, lens sararması\n" +
            "İÇ MEKAN: Koltuk renk/yırtık/leke, tavan döşeme, halı, direksiyon yıpranması\n" +
            "GÖSTERGE: Km gösterge fotoğrafı varsa oku, araç yaşıyla karşılaştır\n" +
            "TAKSİ/FİLO: Sarı renk, arka bölücü plastik yuvası, sticker izi, filoya ait hasar\n" +
            "KAZA TESPİTİ: Farklı boya tonları, kötü işçilik, segment dışı parça değişimi\n" +
            "FOTOĞRAF KALİTESİ: Gizlenen bölge, kasıtlı karanlık köşe, bulanık kritik detay";

        var userText = $"Kategori: {categoryName}\n{declaredInfo}{imageUrls.Count} fotoğrafı profesyonel ekspertiz gözüyle analiz et. Tüm detayları yakala:";
        var contentItems = new List<object>
        {
            new { type = "text", text = userText }
        };

        foreach (var url in imageUrls)
        {
            string imageUrlForApi;
            if (url.StartsWith("/uploads/"))
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "uploads", Path.GetFileName(url));
                if (!File.Exists(filePath)) continue;
                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                var b64 = Convert.ToBase64String(bytes);
                var ext = Path.GetExtension(url).TrimStart('.').ToLowerInvariant();
                var mime = ext == "png" ? "image/png" : ext == "webp" ? "image/webp" : "image/jpeg";
                imageUrlForApi = $"data:{mime};base64,{b64}";
            }
            else
            {
                imageUrlForApi = url;
            }

            contentItems.Add(new
            {
                type = "image_url",
                image_url = new { url = imageUrlForApi, detail = "high" }
            });
        }

        var body = new
        {
            model = _openAi.Model,
            max_tokens = 2000,
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

        var jsonContent = rawContent.Trim();
        if (jsonContent.StartsWith("```"))
        {
            var start = jsonContent.IndexOf('{');
            var end = jsonContent.LastIndexOf('}');
            if (start >= 0 && end > start)
                jsonContent = jsonContent[start..(end + 1)];
        }

        var doc = JsonDocument.Parse(jsonContent).RootElement;

        string? Str(string key) => doc.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        bool Bool(string key) => doc.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;
        int Int(string key, int def) => doc.TryGetProperty(key, out var p) && p.TryGetInt32(out var v) ? v : def;

        var score = Math.Clamp(Int("overallConditionScore", 50), 0, 100);
        var photoQ = Math.Clamp(Int("photoQualityScore", 50), 0, 100);

        List<string> StrList(string key)
        {
            var list = new List<string>();
            if (doc.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        list.Add(item.GetString()!);
            return list;
        }

        var brandMismatch = Bool("brandMismatch");
        var brandMismatchDetail = Str("brandMismatchDetail");

        return new PhotoAnalysisDto(
            OverallConditionScore: score,
            ConditionLabel: Str("conditionLabel") ?? ConditionLabelFor(score),
            DetectedBrand: Str("detectedBrand"),
            DetectedModel: Str("detectedModel"),
            DetectedYear: Str("detectedYear"),
            DetectedColor: Str("detectedColor"),
            DetectedBodyType: Str("detectedBodyType"),
            DetectedFuelType: Str("detectedFuelType"),
            DetectedTransmission: Str("detectedTransmission"),
            DetectedEngineSize: Str("detectedEngineSize"),
            DetectedKmApprox: Str("detectedKmApprox"),
            HasScratchOrDent: Bool("hasScratchOrDent"),
            HasPaintDifference: Bool("hasPaintDifference"),
            HasGlassDamage: Bool("hasGlassDamage"),
            HasWheelOrTireDamage: Bool("hasWheelOrTireDamage"),
            HasRustOrCorrosion: Bool("hasRustOrCorrosion"),
            HasBodyDeformation: Bool("hasBodyDeformation"),
            InteriorDamage: Bool("interiorDamage"),
            HasSeatWear: Bool("hasSeatWear"),
            HasDashboardDamage: Bool("hasDashboardDamage"),
            HasCeilingStain: Bool("hasCeilingStain"),
            SuspectedTaxiOrRental: Bool("suspectedTaxiOrRental"),
            SuspectedAccidentHistory: Bool("suspectedAccidentHistory"),
            SuspectedKmTampering: Bool("suspectedKmTampering"),
            HasHiddenAreas: Bool("hasHiddenAreas"),
            BrandMismatch: brandMismatch,
            BrandMismatchDetail: brandMismatchDetail,
            PhotoQualityScore: photoQ,
            IsProfessionalPhoto: Bool("isProfessionalPhoto"),
            Findings: StrList("findings"),
            Warnings: StrList("warnings"),
            Summary: Str("summary") ?? ""
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fotoğraflardan özellik değeri çıkar (ilan oluşturma akışı için)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Dictionary<string, string>> ExtractAttributesFromPhotosAsync(
        IReadOnlyList<string> imageUrls, string categorySlug, CancellationToken ct = default)
    {
        if (imageUrls.Count == 0 || string.IsNullOrWhiteSpace(_openAi.ApiKey))
            return new Dictionary<string, string>();

        try
        {
            return await CallExtractAttributesAsync(imageUrls, categorySlug, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fotoğraftan özellik çıkarma başarısız: {Message}", ex.Message);
            return new Dictionary<string, string>();
        }
    }

    private static readonly HashSet<string> VehicleSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "otomobil", "arazi-suv-pickup", "elektrikli-araclar", "motosiklet",
        "minivan-panelvan", "ticari-araclar", "karavan", "klasik-araclar", "atv", "deniz-araclari"
    };

    private static readonly HashSet<string> RealEstateSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "konut", "is-yeri", "arsa", "konut-projeleri", "bina", "devre-mulk", "turistik-tesis"
    };

    private async Task<Dictionary<string, string>> CallExtractAttributesAsync(
        IReadOnlyList<string> imageUrls, string categorySlug, CancellationToken ct)
    {
        var isVehicle = VehicleSlugs.Contains(categorySlug);
        var isRealEstate = RealEstateSlugs.Contains(categorySlug);

        string systemPrompt;
        if (isVehicle)
        {
            systemPrompt =
                "Sen uzman bir araç fotoğraf analizörüsün. Verilen araç fotoğraflarından özellik değerlerini tespit et.\n\n" +
                "SADECE şu JSON formatında cevap ver, başka hiçbir şey yazma:\n" +
                "{\n" +
                "  \"brand\": \"<marka slug: acura|alfa-romeo|aston-martin|audi|bentley|bmw|bugatti|byd|cadillac|chevrolet|chrysler|citroen|cupra|dacia|dodge|ds-automobiles|ferrari|fiat|ford|genesis|honda|hyundai|infiniti|jaguar|jeep|kia|lamborghini|lancia|land-rover|lexus|maserati|mazda|mercedes-benz|mini|mitsubishi|nissan|opel|peugeot|porsche|renault|rolls-royce|seat|skoda|smart|subaru|suzuki|tesla|togg|toyota|volkswagen|volvo|diger; tespit edilemezse null>\",\n" +
                "  \"model\": \"<model adı, tespit edilemezse null>\",\n" +
                "  \"year\": \"<4 haneli yıl, örn: 2019; tespit edilemezse null>\",\n" +
                "  \"color\": \"<renk slug: beyaz|siyah|gri|gumus|mavi|lacivert|kirmizi|turuncu|sari|yesil|kahverengi|bej|mor|altin|bordo|diger; null>\",\n" +
                "  \"bodyType\": \"<kasa slug: sedan|hatchback|kombi|suv|crossover|pickup|minivan|coupe|cabrio|van|diger; null>\",\n" +
                "  \"fuel\": \"<yakıt slug: benzin|dizel|lpg|hibrit|plug-in-hibrit|elektrik|benzin-lpg|benzin-cng; null>\",\n" +
                "  \"gear\": \"<vites slug: manuel|otomatik|yarim-otomatik|cvt|dsg; null>\",\n" +
                "  \"condition\": \"<durum slug: sifir|ikinci-el|boyali|degisen|hasar-kayitli; null>\",\n" +
                "  \"km\": \"<sayısal km, sadece rakam, örn: 85000; tespit edilemezse null>\",\n" +
                "  \"engineSize\": \"<cc cinsinden sayısal motor hacmi, örn: 1598; null>\",\n" +
                "  \"enginePower\": \"<HP cinsinden motor gücü sayısal, örn: 120; null>\"\n" +
                "}\n\n" +
                "Önemli: Sadece fotoğraftan GÜVENİLİR ŞEKİLDE tespit edebildiklerini doldur. Emin olmadığın alanları null bırak. Slug değerleri TAM olarak listeden seç.";
        }
        else if (isRealEstate)
        {
            systemPrompt =
                "Sen uzman bir emlak fotoğraf analizörüsün. Verilen emlak fotoğraflarından özellik değerlerini tespit et.\n\n" +
                "SADECE şu JSON formatında cevap ver, başka hiçbir şey yazma:\n" +
                "{\n" +
                "  \"propertyType\": \"<emlak tipi slug: daire|mustakil-ev|villa|residence|yali|ciftlik|prefabrik|yazlik|ofis|dukkan|depo|fabrika|is-hani|plaza|konut-arsasi|ticari-arsa|tarla|bahce|diger; null>\",\n" +
                "  \"grossSqm\": \"<brüt m² sayısal, örn: 120; null>\",\n" +
                "  \"netSqm\": \"<net m² sayısal, örn: 100; null>\",\n" +
                "  \"roomCount\": \"<oda sayısı slug: studio|1-1|1-5|2-1|2-2|3-1|3-2|4-1|4-2|5-1|5-plus; null>\",\n" +
                "  \"buildingAge\": \"<bina yaşı slug: 0|1-5|6-10|11-15|16-20|21-plus; null>\",\n" +
                "  \"floor\": \"<kat slug: bodrum|zemin|bahce-kat|yuksek-zemin|1|2|3|4|5|6|7|8|9|10|11-plus|cati|villa-kat; null>\",\n" +
                "  \"totalFloors\": \"<bina toplam kat sayısı sayısal; null>\",\n" +
                "  \"heating\": \"<ısıtma slug: kombi|merkezi|merkezi-pay|yerden-isitma|klima|soba|soba-dogalgaz|elektrikli|gunes-enerjisi|yok; null>\",\n" +
                "  \"bathCount\": \"<banyo sayısı slug: 1|2|3|4-plus; null>\",\n" +
                "  \"kitchen\": \"<mutfak slug: kapali|acik|amerikan; null>\",\n" +
                "  \"balcony\": \"<balkon var mı: true|false; null>\",\n" +
                "  \"elevator\": \"<asansör var mı: true|false; null>\",\n" +
                "  \"furnished\": \"<eşyalı mı: true|false; null>\",\n" +
                "  \"inSite\": \"<site içinde mi: true|false; null>\"\n" +
                "}\n\n" +
                "Önemli: Sadece fotoğraftan GÜVENİLİR ŞEKİLDE tespit edebildiklerini doldur. Emin olmadığın alanları null bırak. Slug değerleri TAM olarak listeden seç.";
        }
        else
        {
            return new Dictionary<string, string>();
        }

        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        var contentItems = new List<object>
        {
            new { type = "text", text = $"{imageUrls.Count} fotoğrafı analiz et ve özellikleri tespit et." }
        };

        foreach (var url in imageUrls.Take(6))
        {
            string imageUrlForApi;
            if (url.StartsWith("/uploads/"))
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "uploads", Path.GetFileName(url));
                if (!File.Exists(filePath)) continue;
                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                var b64 = Convert.ToBase64String(bytes);
                var ext = Path.GetExtension(url).TrimStart('.').ToLowerInvariant();
                var mime = ext == "png" ? "image/png" : ext == "webp" ? "image/webp" : "image/jpeg";
                imageUrlForApi = $"data:{mime};base64,{b64}";
            }
            else
            {
                imageUrlForApi = url;
            }
            contentItems.Add(new { type = "image_url", image_url = new { url = imageUrlForApi, detail = "high" } });
        }

        var body = new
        {
            model = _openAi.Model,
            max_tokens = 800,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = contentItems }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        OpenAiErrorMapper.EnsureSuccess(resp, raw);

        var root = JsonDocument.Parse(raw).RootElement;
        var rawContent = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";

        var jsonContent = rawContent.Trim();
        if (jsonContent.StartsWith("```"))
        {
            var start = jsonContent.IndexOf('{');
            var end = jsonContent.LastIndexOf('}');
            if (start >= 0 && end > start) jsonContent = jsonContent[start..(end + 1)];
        }

        var doc = JsonDocument.Parse(jsonContent).RootElement;
        var result = new Dictionary<string, string>();

        foreach (var prop in doc.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Null) continue;
            var val = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? prop.Value.GetRawText()
                    : prop.Value.GetRawText();
            if (!string.IsNullOrWhiteSpace(val))
                result[prop.Name] = val!;
        }

        return result;
    }

    private static PhotoAnalysisDto NoPhotoResult() => new(
        OverallConditionScore: 0, ConditionLabel: "Bilinmiyor",
        DetectedBrand: null, DetectedModel: null, DetectedYear: null,
        DetectedColor: null, DetectedBodyType: null, DetectedFuelType: null,
        DetectedTransmission: null, DetectedEngineSize: null, DetectedKmApprox: null,
        HasScratchOrDent: false, HasPaintDifference: false, HasGlassDamage: false,
        HasWheelOrTireDamage: false, HasRustOrCorrosion: false, HasBodyDeformation: false,
        InteriorDamage: false, HasSeatWear: false, HasDashboardDamage: false, HasCeilingStain: false,
        SuspectedTaxiOrRental: false, SuspectedAccidentHistory: false,
        SuspectedKmTampering: false, HasHiddenAreas: false,
        BrandMismatch: false, BrandMismatchDetail: null,
        PhotoQualityScore: 0, IsProfessionalPhoto: false,
        Findings: [], Warnings: ["Bu ilan için fotoğraf bulunmuyor."],
        Summary: "Fotoğraf eklenmemiş, görsel analiz yapılamadı."
    );

    private static PhotoAnalysisDto BuildFallbackAnalysis(int imageCount) => new(
        OverallConditionScore: 50, ConditionLabel: "Orta",
        DetectedBrand: null, DetectedModel: null, DetectedYear: null,
        DetectedColor: null, DetectedBodyType: null, DetectedFuelType: null,
        DetectedTransmission: null, DetectedEngineSize: null, DetectedKmApprox: null,
        HasScratchOrDent: false, HasPaintDifference: false, HasGlassDamage: false,
        HasWheelOrTireDamage: false, HasRustOrCorrosion: false, HasBodyDeformation: false,
        InteriorDamage: false, HasSeatWear: false, HasDashboardDamage: false, HasCeilingStain: false,
        SuspectedTaxiOrRental: false, SuspectedAccidentHistory: false,
        SuspectedKmTampering: false, HasHiddenAreas: false,
        BrandMismatch: false, BrandMismatchDetail: null,
        PhotoQualityScore: 0, IsProfessionalPhoto: false,
        Findings: [$"{imageCount} fotoğraf mevcut."],
        Warnings: [],
        Summary: "AI analiz servisi geçici olarak kullanılamıyor."
    );

    private static string ConditionLabelFor(int score) => score switch
    {
        >= 90 => "Çok İyi",
        >= 75 => "İyi",
        >= 55 => "Orta",
        _ => "Kötü"
    };
}
