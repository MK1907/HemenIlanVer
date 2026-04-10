using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HemenIlanVer.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        await db.Database.MigrateAsync(ct);

        if (!await db.Cities.AnyAsync(ct))
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var r in new[] { "User", "Admin" })
                if (!await roleManager.RoleExistsAsync(r))
                    await roleManager.CreateAsync(new IdentityRole<Guid>(r) { Id = Guid.NewGuid() });

            var cityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001");
            var distId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0002");
            db.Cities.Add(new Domain.Entities.City { Id = cityId, Name = "İstanbul", Slug = "istanbul", PlateCode = 34, CreatedAt = DateTimeOffset.UtcNow });
            db.Districts.Add(new Domain.Entities.District { Id = distId, CityId = cityId, Name = "Ataşehir", Slug = "atasehir", CreatedAt = DateTimeOffset.UtcNow });

            var demo = new ApplicationUser
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddd0001"),
                UserName = "demo@hemenilanver.local",
                Email = "demo@hemenilanver.local",
                EmailConfirmed = true,
                DisplayName = "Demo Kullanıcı",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await userManager.CreateAsync(demo, "Demo12345!");
            await userManager.AddToRoleAsync(demo, "User");

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Veritabanı ilk tohum verisi yüklendi (şehir, roller, demo kullanıcı). Kategoriler AI tarafından oluşturulacak.");
        }

        await SeedSubcategoriesAsync(db, logger, ct);
        await SeedVehicleAttributesAsync(db, logger, ct);
        await SeedRealEstateAttributesAsync(db, logger, ct);
    }

    /// <summary>
    /// Araç ve Emlak root kategorileri altına sahibinden.com tarzı sibling kategoriler ekler.
    /// Yanlış yere (otomobil altı) eklenmiş grandchild'ları temizler.
    /// Her startup'ta idempotent çalışır.
    /// </summary>
    private static async Task SeedSubcategoriesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        // Araç root'u altındaki sibling kategoriler
        var aracSubcats = new[]
        {
            ("Otomobil",           "otomobil"),
            ("Arazi, SUV & Pickup","arazi-suv-pickup"),
            ("Elektrikli Araçlar", "elektrikli-araclar"),
            ("Motosiklet",         "motosiklet"),
            ("Minivan & Panelvan", "minivan-panelvan"),
            ("Ticari Araçlar",     "ticari-araclar"),
            ("Karavan",            "karavan"),
            ("Deniz Araçları",     "deniz-araclari"),
            ("Klasik Araçlar",     "klasik-araclar"),
            ("ATV",                "atv"),
        };

        // Emlak root'u altındaki sibling kategoriler (sahibinden.com tarzı)
        var emlakSubcats = new[]
        {
            ("Konut",              "konut"),
            ("İş Yeri",            "is-yeri"),
            ("Arsa",               "arsa"),
            ("Konut Projeleri",    "konut-projeleri"),
            ("Bina",               "bina"),
            ("Devre Mülk",         "devre-mulk"),
            ("Turistik Tesis",     "turistik-tesis"),
        };

        var all = await db.Categories.ToListAsync(ct);

        // ── Araç root'unu bul ──────────────────────────────────────────────
        var aracRoot = all.FirstOrDefault(c =>
            c.ParentId == null &&
            (c.Slug.Contains("arac") || c.Slug.Contains("vasit") || c.Slug == "arac-vasita"));

        if (aracRoot is not null)
        {
            // Önceki yanlış implementasyondan kalan grandchild'ları temizle
            // (Otomobil altındaki SUV, Sedan, Hatchback, Ticari Araç, Minivan MPV, Pick-up, Cabrio)
            var wrongSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "suv","sedan","hatchback","ticari-arac","minivan-mpv","pick-up","cabrio",
                  "mustakil-ev","koy-evi","residence","yazlik" };

            var grandchildren = all.Where(c =>
            {
                var parent = all.FirstOrDefault(p => p.Id == c.ParentId);
                return parent?.ParentId == aracRoot.Id && wrongSlugs.Contains(c.Slug);
            }).ToList();

            if (grandchildren.Count > 0)
            {
                db.Categories.RemoveRange(grandchildren);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Yanlış konumdaki {Count} grandchild kategori temizlendi.", grandchildren.Count);
            }

            // Sibling kategorileri root altına ekle
            // DB'yi yeniden yükle (cleanup sonrası güncel hali al)
            all = await db.Categories.ToListAsync(ct);
            var globalSlugs = all.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directChildren = all.Where(c => c.ParentId == aracRoot.Id).Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sort = directChildren.Count;
            var added = 0;
            foreach (var (name, slug) in aracSubcats)
            {
                if (directChildren.Contains(slug)) continue; // zaten doğru yerde

                if (globalSlugs.Contains(slug))
                {
                    // Yanlış parent altında → parent'ı düzelt
                    var existing = all.FirstOrDefault(c => c.Slug == slug);
                    if (existing is not null)
                    {
                        existing.ParentId = aracRoot.Id;
                        existing.SortOrder = ++sort;
                    }
                    continue;
                }

                db.Categories.Add(new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                    ParentId = aracRoot.Id,
                    SortOrder = ++sort,
                    IsActive = true,
                    DefaultListingType = Domain.Enums.ListingType.Satilik,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                added++;
            }

            if (added > 0 || db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Araç kategorisine {Count} yeni alt kategori eklendi/güncellendi.", added);
            }
        }

        // ── Emlak root'unu bul ─────────────────────────────────────────────
        var emlakRoot = all.FirstOrDefault(c =>
            c.ParentId == null &&
            (c.Slug.Contains("emlak") || c.Slug == "konut" || c.Slug == "gayrimenkul"));

        if (emlakRoot is not null)
        {
            // Önceki yanlış eklenen grandchild'ları ve istemediğimiz kategorileri temizle
            var wrongEmlakSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "daire","villa","mustakil-ev","koy-evi","residence","yazlik",
                  "isyeri-ofis","dukkan-magaza","depo-antrepo" };

            var wrongGrandchildren = all.Where(c =>
            {
                var parent = all.FirstOrDefault(p => p.Id == c.ParentId);
                return parent?.ParentId == emlakRoot.Id && wrongEmlakSlugs.Contains(c.Slug);
            }).ToList();

            if (wrongGrandchildren.Count > 0)
            {
                db.Categories.RemoveRange(wrongGrandchildren);
                await db.SaveChangesAsync(ct);
            }

            all = await db.Categories.ToListAsync(ct);
            var globalSlugsEmlak = all.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directChildrenEmlak = all.Where(c => c.ParentId == emlakRoot.Id).Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sort = directChildrenEmlak.Count;
            var added = 0;
            foreach (var (name, slug) in emlakSubcats)
            {
                if (directChildrenEmlak.Contains(slug)) continue;

                if (globalSlugsEmlak.Contains(slug))
                {
                    var existing = all.FirstOrDefault(c => c.Slug == slug);
                    if (existing is not null)
                    {
                        existing.ParentId = emlakRoot.Id;
                        existing.SortOrder = ++sort;
                    }
                    continue;
                }

                db.Categories.Add(new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                    ParentId = emlakRoot.Id,
                    SortOrder = ++sort,
                    IsActive = true,
                    DefaultListingType = Domain.Enums.ListingType.Satilik,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                added++;
            }

            if (added > 0 || db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Emlak kategorisine {Count} yeni alt kategori eklendi/güncellendi.", added);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Araç kategorilerine standart 18 özellik seed et (idempotent)
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedVehicleAttributesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var vehicleSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "otomobil", "arazi-suv-pickup", "elektrikli-araclar", "motosiklet",
            "minivan-panelvan", "ticari-araclar", "karavan", "klasik-araclar", "atv", "deniz-araclari"
        };

        var allCats = await db.Categories.Where(c => c.IsActive).ToListAsync(ct);
        var vehicleCats = allCats.Where(c => vehicleSlugs.Contains(c.Slug)).ToList();
        if (vehicleCats.Count == 0) return;

        var existingAttrs = await db.CategoryAttributes
            .Include(a => a.Options)
            .Where(a => vehicleCats.Select(c => c.Id).Contains(a.CategoryId))
            .ToListAsync(ct);

        var totalAdded = 0;

        foreach (var cat in vehicleCats)
        {
            var catAttrs = existingAttrs.Where(a => a.CategoryId == cat.Id).ToList();
            var existingKeys = catAttrs.Select(a => a.AttributeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var defs = BuildVehicleAttributeDefs(cat.Slug);

            foreach (var def in defs)
            {
                if (existingKeys.Contains(def.Key)) continue;

                var attr = new CategoryAttribute
                {
                    Id = Guid.NewGuid(),
                    CategoryId = cat.Id,
                    AttributeKey = def.Key,
                    DisplayName = def.Display,
                    DataType = def.DataType,
                    IsRequired = def.Required,
                    SortOrder = def.Sort,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                foreach (var (optKey, optLabel, parentKey) in def.Options)
                {
                    attr.Options.Add(new CategoryAttributeOption
                    {
                        Id = Guid.NewGuid(),
                        CategoryAttributeId = attr.Id,
                        ValueKey = optKey,
                        Label = optLabel,
                        ParentOptionId = null,
                        SortOrder = attr.Options.Count,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                db.CategoryAttributes.Add(attr);
                totalAdded++;
            }
        }

        if (totalAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Araç kategorilerine {Count} yeni standart özellik eklendi.", totalAdded);
        }
    }

    private record AttrDef(
        string Key, string Display,
        Domain.Enums.AttributeDataType DataType,
        bool Required, int Sort,
        (string Key, string Label, string? ParentKey)[] Options);

    private static List<AttrDef> BuildVehicleAttributeDefs(string catSlug)
    {
        // ── Ortak yardımcılar ────────────────────────────────────────────────
        (string, string, string?)[] Opts(params (string, string, string?)[] o) => o;

        var markalar = new[]
        {
            "Acura","Alfa Romeo","Aston Martin","Audi","Bentley","BMW","Bugatti","BYD",
            "Cadillac","Chevrolet","Chrysler","Citroën","Cupra","Dacia","Dodge","DS Automobiles",
            "Ferrari","Fiat","Ford","Genesis","Honda","Hyundai","Infiniti","Jaguar",
            "Jeep","Kia","Lamborghini","Lancia","Land Rover","Lexus","Maserati",
            "Mazda","Mercedes-Benz","Mini","Mitsubishi","Nissan","Opel","Peugeot","Porsche",
            "Renault","Rolls-Royce","SEAT","Skoda","Smart","Subaru","Suzuki","Tesla",
            "TOGG","Toyota","Volkswagen","Volvo","Diğer"
        };
        var markaOpts = markalar.Select(m => (Slugify(m), m, (string?)null)).ToArray();

        var yillar = Enumerable.Range(1970, DateTime.UtcNow.Year - 1970 + 1)
            .Reverse().Select(y => (y.ToString(), y.ToString(), (string?)null)).ToArray();

        var renkler = Opts(
            ("beyaz","Beyaz",(string?)null),("siyah","Siyah",null),("gri","Gri",null),
            ("gumus","Gümüş",null),("mavi","Mavi",null),("lacivert","Lacivert",null),
            ("kirmizi","Kırmızı",null),("turuncu","Turuncu",null),("sari","Sarı",null),
            ("yesil","Yeşil",null),("kahverengi","Kahverengi",null),("bej","Bej",null),
            ("mor","Mor",null),("altin","Altın / Şampanya",null),("bordo","Bordo",null),("diger","Diğer",null));

        var yakitStandart = Opts(
            ("benzin","Benzin",(string?)null),("dizel","Dizel",null),("lpg","LPG",null),
            ("hibrit","Hibrit",null),("plug-in-hibrit","Plug-in Hibrit",null),
            ("elektrik","Elektrik",null),("benzin-lpg","Benzin & LPG",null),("benzin-cng","Benzin & CNG",null));

        var vitesStandart = Opts(
            ("manuel","Manuel",(string?)null),("otomatik","Otomatik",null),
            ("yarim-otomatik","Yarı Otomatik",null),("cvt","CVT",null),("dsg","DSG/DCT",null));

        var cekisOpts = Opts(
            ("onden","Önden Çekiş",(string?)null),("arkadan","Arkadan İtiş",null),
            ("dort-dort","4x4 Tam Zamanlı",null),("dort-iki","4x2",null),("awd","AWD",null));

        var durumOpts = Opts(
            ("sifir","Sıfır",(string?)null),("ikinci-el","İkinci El",null),
            ("boyali","Boyalı",null),("degisen","Değişen",null),("hasar-kayitli","Hasar Kayıtlı",null));

        var plakaOpts = Opts(("tr","Türkiye",(string?)null),("avrupa","Avrupa",null),("diger","Diğer",null));
        var saticiOpts = Opts(("sahibinden","Sahibinden",(string?)null),("galeriden","Galeriden",null));

        // ── Kategoriye özel listeler ─────────────────────────────────────────
        return catSlug switch
        {
            // ── Otomobil ────────────────────────────────────────────────────
            "otomobil" or "klasik-araclar" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("series",      "Seri",                Domain.Enums.AttributeDataType.String, false, 2, []),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 3, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  4, yillar),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 5, yakitStandart),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 6, vitesStandart),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  7, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 8, []),
                new("bodyType",    "Kasa Tipi",           Domain.Enums.AttributeDataType.Enum,   false, 9, Opts(
                    ("sedan","Sedan",(string?)null),("hatchback","Hatchback",null),("kombi","Station Wagon / Kombi",null),
                    ("suv","SUV",null),("crossover","Crossover",null),("pickup","Pick-up",null),
                    ("minivan","Minivan / MPV",null),("coupe","Coupe",null),("cabrio","Cabriolet / Roadster",null),
                    ("van","Van",null),("diger","Diğer",null))),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("engineSize",  "Motor Hacmi (cc)",    Domain.Enums.AttributeDataType.Int,    false, 11, []),
                new("drive",       "Çekiş",               Domain.Enums.AttributeDataType.Enum,   false, 12, cekisOpts),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 13, renkler),
                new("warranty",    "Garanti",             Domain.Enums.AttributeDataType.Bool,   false, 14, []),
                new("heavyDamage", "Ağır Hasar Kayıtlı", Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("plate",       "Plaka / Uyruk",       Domain.Enums.AttributeDataType.Enum,   false, 16, plakaOpts),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 17, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 18, []),
            },

            // ── Arazi / SUV / Pickup ────────────────────────────────────────
            "arazi-suv-pickup" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("series",      "Seri",                Domain.Enums.AttributeDataType.String, false, 2, []),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 3, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  4, yillar),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 5, yakitStandart),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 6, vitesStandart),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  7, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 8, []),
                new("bodyType",    "Kasa Tipi",           Domain.Enums.AttributeDataType.Enum,   false, 9, Opts(
                    ("suv","SUV",(string?)null),("arazi","Arazi",null),("pickup","Pick-up",null),
                    ("crossover","Crossover",null),("van","Van",null),("diger","Diğer",null))),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("engineSize",  "Motor Hacmi (cc)",    Domain.Enums.AttributeDataType.Int,    false, 11, []),
                new("drive",       "Çekiş",               Domain.Enums.AttributeDataType.Enum,   false, 12, cekisOpts),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 13, renkler),
                new("warranty",    "Garanti",             Domain.Enums.AttributeDataType.Bool,   false, 14, []),
                new("heavyDamage", "Ağır Hasar Kayıtlı", Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("plate",       "Plaka / Uyruk",       Domain.Enums.AttributeDataType.Enum,   false, 16, plakaOpts),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 17, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 18, []),
            },

            // ── Elektrikli Araçlar ──────────────────────────────────────────
            "elektrikli-araclar" => new List<AttrDef>
            {
                new("brand",           "Marka",                  Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("series",          "Seri",                   Domain.Enums.AttributeDataType.String, false, 2, []),
                new("model",           "Model",                  Domain.Enums.AttributeDataType.String, false, 3, []),
                new("year",            "Yıl",                    Domain.Enums.AttributeDataType.Enum,   true,  4, yillar),
                new("gear",            "Vites",                  Domain.Enums.AttributeDataType.Enum,   false, 5, Opts(
                    ("otomatik","Otomatik (Tek Vitesli)",(string?)null),("diger","Diğer",null))),
                new("condition",       "Araç Durumu",            Domain.Enums.AttributeDataType.Enum,   true,  6, durumOpts),
                new("km",              "Kilometre",              Domain.Enums.AttributeDataType.Int,    false, 7, []),
                new("bodyType",        "Kasa Tipi",              Domain.Enums.AttributeDataType.Enum,   false, 8, Opts(
                    ("sedan","Sedan",(string?)null),("hatchback","Hatchback",null),("suv","SUV",null),
                    ("crossover","Crossover",null),("kombi","Kombi",null),("coupe","Coupe",null),("diger","Diğer",null))),
                new("enginePower",     "Motor Gücü (HP)",        Domain.Enums.AttributeDataType.Int,    false, 9, []),
                new("range",           "Menzil (km)",            Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("batteryCapacity", "Batarya Kapasitesi (kWh)", Domain.Enums.AttributeDataType.Int,  false, 11, []),
                new("chargingTime",    "Şarj Süresi (dk)",       Domain.Enums.AttributeDataType.Int,    false, 12, []),
                new("drive",           "Çekiş",                  Domain.Enums.AttributeDataType.Enum,   false, 13, cekisOpts),
                new("color",           "Renk",                   Domain.Enums.AttributeDataType.Enum,   false, 14, renkler),
                new("warranty",        "Garanti",                Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("heavyDamage",     "Ağır Hasar Kayıtlı",    Domain.Enums.AttributeDataType.Bool,   false, 16, []),
                new("plate",           "Plaka / Uyruk",          Domain.Enums.AttributeDataType.Enum,   false, 17, plakaOpts),
                new("seller",          "Kimden",                 Domain.Enums.AttributeDataType.Enum,   false, 18, saticiOpts),
                new("tradeIn",         "Takas",                  Domain.Enums.AttributeDataType.Bool,   false, 19, []),
            },

            // ── Motosiklet ──────────────────────────────────────────────────
            "motosiklet" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 2, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  3, yillar),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 4, Opts(
                    ("benzin","Benzin",(string?)null),("elektrik","Elektrik",null),
                    ("lpg","LPG",null),("diger","Diğer",null))),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 5, Opts(
                    ("manuel","Manuel",(string?)null),("otomatik","Otomatik",null),("yarim-otomatik","Yarı Otomatik",null))),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  6, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 7, []),
                new("bodyType",    "Tip",                 Domain.Enums.AttributeDataType.Enum,   false, 8, Opts(
                    ("naked","Naked",(string?)null),("sport","Sport / Supersport",null),("touring","Touring",null),
                    ("enduro","Enduro / Off-Road",null),("chopper","Chopper / Cruiser",null),
                    ("scooter","Scooter",null),("motocross","Motocross",null),("diger","Diğer",null))),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 9, []),
                new("engineSize",  "Motor Hacmi (cc)",    Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 11, renkler),
                new("warranty",    "Garanti",             Domain.Enums.AttributeDataType.Bool,   false, 12, []),
                new("plate",       "Plaka / Uyruk",       Domain.Enums.AttributeDataType.Enum,   false, 13, plakaOpts),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 14, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 15, []),
            },

            // ── Minivan & Panelvan ──────────────────────────────────────────
            "minivan-panelvan" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("series",      "Seri",                Domain.Enums.AttributeDataType.String, false, 2, []),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 3, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  4, yillar),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 5, yakitStandart),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 6, vitesStandart),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  7, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 8, []),
                new("bodyType",    "Kasa Tipi",           Domain.Enums.AttributeDataType.Enum,   false, 9, Opts(
                    ("minivan","Minivan",(string?)null),("panelvan","Panelvan",null),
                    ("kombi","Kombi",null),("diger","Diğer",null))),
                new("seatCount",   "Koltuk Sayısı",       Domain.Enums.AttributeDataType.Enum,   false, 10, Opts(
                    ("5","5 Koltuk",(string?)null),("6","6 Koltuk",null),("7","7 Koltuk",null),
                    ("8","8 Koltuk",null),("9","9 Koltuk",null),("diger","Diğer",null))),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 11, []),
                new("engineSize",  "Motor Hacmi (cc)",    Domain.Enums.AttributeDataType.Int,    false, 12, []),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 13, renkler),
                new("warranty",    "Garanti",             Domain.Enums.AttributeDataType.Bool,   false, 14, []),
                new("heavyDamage", "Ağır Hasar Kayıtlı", Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("plate",       "Plaka / Uyruk",       Domain.Enums.AttributeDataType.Enum,   false, 16, plakaOpts),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 17, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 18, []),
            },

            // ── Ticari Araçlar ──────────────────────────────────────────────
            "ticari-araclar" => new List<AttrDef>
            {
                new("brand",         "Marka",                  Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("series",        "Seri",                   Domain.Enums.AttributeDataType.String, false, 2, []),
                new("model",         "Model",                  Domain.Enums.AttributeDataType.String, false, 3, []),
                new("year",          "Yıl",                    Domain.Enums.AttributeDataType.Enum,   true,  4, yillar),
                new("fuel",          "Yakıt",                  Domain.Enums.AttributeDataType.Enum,   false, 5, yakitStandart),
                new("gear",          "Vites",                  Domain.Enums.AttributeDataType.Enum,   false, 6, vitesStandart),
                new("condition",     "Araç Durumu",            Domain.Enums.AttributeDataType.Enum,   true,  7, durumOpts),
                new("km",            "Kilometre",              Domain.Enums.AttributeDataType.Int,    false, 8, []),
                new("bodyType",      "Araç Tipi",              Domain.Enums.AttributeDataType.Enum,   false, 9, Opts(
                    ("kamyonet","Kamyonet",(string?)null),("kamyon","Kamyon",null),("minibus","Minibüs",null),
                    ("otobus","Otobüs",null),("cekici","Çekici / TIR",null),("frigorifik","Frigorifik",null),
                    ("tanker","Tanker",null),("mikser","Mikser",null),("diger","Diğer",null))),
                new("enginePower",   "Motor Gücü (HP)",        Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("engineSize",    "Motor Hacmi (cc)",       Domain.Enums.AttributeDataType.Int,    false, 11, []),
                new("payload",       "Yük Kapasitesi (kg)",    Domain.Enums.AttributeDataType.Int,    false, 12, []),
                new("drive",         "Çekiş",                  Domain.Enums.AttributeDataType.Enum,   false, 13, cekisOpts),
                new("color",         "Renk",                   Domain.Enums.AttributeDataType.Enum,   false, 14, renkler),
                new("heavyDamage",   "Ağır Hasar Kayıtlı",    Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("plate",         "Plaka / Uyruk",          Domain.Enums.AttributeDataType.Enum,   false, 16, plakaOpts),
                new("seller",        "Kimden",                 Domain.Enums.AttributeDataType.Enum,   false, 17, saticiOpts),
                new("tradeIn",       "Takas",                  Domain.Enums.AttributeDataType.Bool,   false, 18, []),
            },

            // ── Karavan ─────────────────────────────────────────────────────
            "karavan" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 2, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  3, yillar),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  4, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 5, []),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 6, yakitStandart),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 7, vitesStandart),
                new("bodyType",    "Karavan Tipi",        Domain.Enums.AttributeDataType.Enum,   false, 8, Opts(
                    ("motorlu","Motorlu Karavan",(string?)null),("cekilir","Çekilir Karavan",null),
                    ("kamp-arabasi","Kamp Arabası",null),("diger","Diğer",null))),
                new("length",      "Uzunluk (m)",         Domain.Enums.AttributeDataType.Decimal, false, 9, []),
                new("bedCount",    "Yatak Sayısı",        Domain.Enums.AttributeDataType.Enum,   false, 10, Opts(
                    ("1","1",(string?)null),("2","2",null),("3","3",null),("4-plus","4+",null))),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 11, renkler),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 12, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 13, []),
            },

            // ── Deniz Araçları ───────────────────────────────────────────────
            "deniz-araclari" => new List<AttrDef>
            {
                new("brand",       "Marka / Tersane",     Domain.Enums.AttributeDataType.String, false, 1, []),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 2, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  3, yillar),
                new("condition",   "Durum",               Domain.Enums.AttributeDataType.Enum,   true,  4, Opts(
                    ("sifir","Sıfır",(string?)null),("ikinci-el","İkinci El",null),("hasar-kayitli","Hasar Kayıtlı",null))),
                new("bodyType",    "Tekne Tipi",          Domain.Enums.AttributeDataType.Enum,   false, 5, Opts(
                    ("motor-yat","Motor Yat",(string?)null),("yelkenli","Yelkenli",null),
                    ("surat-teknesi","Sürat Teknesi",null),("sisme","Şişme Bot",null),
                    ("balıkci","Balıkçı Teknesi",null),("katamaran","Katamaran",null),
                    ("gulet","Gulet",null),("jet-ski","Jet-Ski / Su Motosikleti",null),
                    ("diger","Diğer",null))),
                new("length",      "Uzunluk (m)",         Domain.Enums.AttributeDataType.Decimal, false, 6, []),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 7, []),
                new("engineCount", "Motor Adedi",         Domain.Enums.AttributeDataType.Enum,   false, 8, Opts(
                    ("1","1 Motor",(string?)null),("2","2 Motor",null),("motorsuz","Motorsuz",null))),
                new("material",    "Tekne Malzemesi",     Domain.Enums.AttributeDataType.Enum,   false, 9, Opts(
                    ("fibergl","Fiberglas",(string?)null),("ahsap","Ahşap",null),("metal","Metal / Çelik",null),
                    ("aluminyum","Alüminyum",null),("sisme","Şişme",null),("diger","Diğer",null))),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 10, renkler),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 11, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 12, []),
            },

            // ── ATV ─────────────────────────────────────────────────────────
            "atv" => new List<AttrDef>
            {
                new("brand",       "Marka",               Domain.Enums.AttributeDataType.Enum,   true,  1, markaOpts),
                new("model",       "Model",               Domain.Enums.AttributeDataType.String, false, 2, []),
                new("year",        "Yıl",                 Domain.Enums.AttributeDataType.Enum,   true,  3, yillar),
                new("fuel",        "Yakıt",               Domain.Enums.AttributeDataType.Enum,   false, 4, Opts(
                    ("benzin","Benzin",(string?)null),("elektrik","Elektrik",null),("diger","Diğer",null))),
                new("gear",        "Vites",               Domain.Enums.AttributeDataType.Enum,   false, 5, Opts(
                    ("manuel","Manuel",(string?)null),("otomatik","Otomatik",null),("yarim-otomatik","Yarı Otomatik",null))),
                new("condition",   "Araç Durumu",         Domain.Enums.AttributeDataType.Enum,   true,  6, durumOpts),
                new("km",          "Kilometre",           Domain.Enums.AttributeDataType.Int,    false, 7, []),
                new("bodyType",    "Tip",                 Domain.Enums.AttributeDataType.Enum,   false, 8, Opts(
                    ("atv","ATV",(string?)null),("utv","UTV / Side-by-Side",null),
                    ("buggy","Buggy",null),("diger","Diğer",null))),
                new("enginePower", "Motor Gücü (HP)",     Domain.Enums.AttributeDataType.Int,    false, 9, []),
                new("engineSize",  "Motor Hacmi (cc)",    Domain.Enums.AttributeDataType.Int,    false, 10, []),
                new("color",       "Renk",                Domain.Enums.AttributeDataType.Enum,   false, 11, renkler),
                new("seller",      "Kimden",              Domain.Enums.AttributeDataType.Enum,   false, 12, saticiOpts),
                new("tradeIn",     "Takas",               Domain.Enums.AttributeDataType.Bool,   false, 13, []),
            },

            _ => new List<AttrDef>()
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Emlak kategorilerine standart özellikler seed et (idempotent)
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedRealEstateAttributesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var emlakSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "konut", "is-yeri", "arsa", "konut-projeleri", "bina", "devre-mulk", "turistik-tesis"
        };

        var allCats = await db.Categories.Where(c => c.IsActive).ToListAsync(ct);
        var emlakCats = allCats.Where(c => emlakSlugs.Contains(c.Slug)).ToList();
        if (emlakCats.Count == 0) return;

        var existingAttrs = await db.CategoryAttributes
            .Include(a => a.Options)
            .Where(a => emlakCats.Select(c => c.Id).Contains(a.CategoryId))
            .ToListAsync(ct);

        var totalAdded = 0;

        foreach (var cat in emlakCats)
        {
            var catAttrs = existingAttrs.Where(a => a.CategoryId == cat.Id).ToList();
            var existingKeys = catAttrs.Select(a => a.AttributeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var defs = BuildRealEstateAttributeDefs(cat.Slug);

            foreach (var def in defs)
            {
                if (existingKeys.Contains(def.Key)) continue;

                var attr = new CategoryAttribute
                {
                    Id = Guid.NewGuid(),
                    CategoryId = cat.Id,
                    AttributeKey = def.Key,
                    DisplayName = def.Display,
                    DataType = def.DataType,
                    IsRequired = def.Required,
                    SortOrder = def.Sort,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                foreach (var (optKey, optLabel, _) in def.Options)
                {
                    attr.Options.Add(new CategoryAttributeOption
                    {
                        Id = Guid.NewGuid(),
                        CategoryAttributeId = attr.Id,
                        ValueKey = optKey,
                        Label = optLabel,
                        ParentOptionId = null,
                        SortOrder = attr.Options.Count,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }

                db.CategoryAttributes.Add(attr);
                totalAdded++;
            }
        }

        if (totalAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Emlak kategorilerine {Count} yeni standart özellik eklendi.", totalAdded);
        }
    }

    private static List<AttrDef> BuildRealEstateAttributeDefs(string catSlug)
    {
        (string, string, string?)[] Opts(params (string, string, string?)[] o) => o;

        // ── Ortak seçenekler ─────────────────────────────────────────────────
        var binaYaslari = Opts(
            ("0","Sıfır / İnşaat Halinde",(string?)null),("1-5","1-5 Yıl",null),
            ("6-10","6-10 Yıl",null),("11-15","11-15 Yıl",null),
            ("16-20","16-20 Yıl",null),("21-plus","21 Yıl ve Üzeri",null));

        var katlar = Opts(
            ("bodrum","Bodrum Kat",(string?)null),("zemin","Zemin Kat",null),
            ("bahce-kat","Bahçe Katı",null),("yuksek-zemin","Yüksek Giriş",null),
            ("1","1. Kat",null),("2","2. Kat",null),("3","3. Kat",null),
            ("4","4. Kat",null),("5","5. Kat",null),("6","6. Kat",null),
            ("7","7. Kat",null),("8","8. Kat",null),("9","9. Kat",null),
            ("10","10. Kat",null),("11-plus","11. Kat ve Üzeri",null),
            ("cati","Çatı Katı / Dublex",null),("villa-kat","Villa Katı",null));

        var isiitmaTipleri = Opts(
            ("kombi","Kombi (Doğalgaz)",(string?)null),("merkezi","Merkezi Sistem",null),
            ("merkezi-pay","Merkezi (Pay Ölçer)",null),("yerden-isitma","Yerden Isıtma",null),
            ("klima","Klima",null),("soba","Soba",null),("soba-dogalgaz","Soba (Doğalgaz)",null),
            ("elektrikli","Elektrikli Radyatör",null),("gunes-enerjisi","Güneş Enerjisi",null),("yok","Isıtma Yok",null));

        var banyoSayilari = Opts(
            ("1","1",(string?)null),("2","2",null),("3","3",null),("4-plus","4 ve Üzeri",null));

        var mutfakTipleri = Opts(
            ("kapali","Kapalı Mutfak",(string?)null),("acik","Açık Mutfak",null),("amerikan","Amerikan Mutfak",null));

        var otoparkTipleri = Opts(
            ("yok","Yok",(string?)null),("acik","Açık Otopark",null),
            ("kapali","Kapalı Otopark",null),("acik-kapali","Açık & Kapalı Otopark",null));

        var kullanımDurumu = Opts(
            ("bos","Boş",(string?)null),("kiraciyla","Kiracılı",null),("mal-sahibi","Mal Sahibi Kullanıyor",null));

        var tapuDurumu = Opts(
            ("kat-mulkiyetli","Kat Mülkiyetli",(string?)null),("kat-irtifakli","Kat İrtifaklı",null),
            ("arsa-tapulu","Arsa Tapulu",null),("hisseli","Hisseli Tapu",null),
            ("musha","Müşterek",null),("tahsisli","Tahsisli",null),("diger","Diğer",null));

        var kimden = Opts(
            ("sahibinden","Sahibinden",(string?)null),
            ("emlak-ofisinden","Emlak Ofisinden",null),
            ("insaat-firmasi","İnşaat Firmasından",null));

        var odaSayilari = Opts(
            ("studio","Stüdyo / 1+0",(string?)null),("1-1","1+1",null),("1-5","1,5+1",null),
            ("2-1","2+1",null),("2-2","2+2",null),("3-1","3+1",null),("3-2","3+2",null),
            ("4-1","4+1",null),("4-2","4+2",null),("5-1","5+1",null),("5-plus","5+2 ve üzeri",null));

        var cepheOpts = Opts(
            ("kuzey","Kuzey",(string?)null),("guney","Güney",null),("dogu","Doğu",null),
            ("bati","Batı",null),("kuzey-dogu","Kuzey-Doğu",null),("kuzey-bati","Kuzey-Batı",null),
            ("guney-dogu","Güney-Doğu",null),("guney-bati","Güney-Batı",null));

        return catSlug switch
        {
            // ── Konut ───────────────────────────────────────────────────────
            "konut" => new List<AttrDef>
            {
                new("listingType",  "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("propertyType", "Konut Tipi",        Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("daire","Daire",(string?)null),("mustakil-ev","Müstakil Ev",null),
                    ("villa","Villa",null),("residence","Residence",null),
                    ("yali","Yalı / Köşk",null),("ciftlik","Çiftlik Evi",null),
                    ("prefabrik","Prefabrik Ev",null),("yazlik","Yazlık",null))),
                new("grossSqm",     "m² (Brüt)",         Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("netSqm",       "m² (Net)",          Domain.Enums.AttributeDataType.Int,    false, 3, []),
                new("roomCount",    "Oda Sayısı",        Domain.Enums.AttributeDataType.Enum,   false, 4, odaSayilari),
                new("buildingAge",  "Bina Yaşı",         Domain.Enums.AttributeDataType.Enum,   false, 5, binaYaslari),
                new("floor",        "Bulunduğu Kat",     Domain.Enums.AttributeDataType.Enum,   false, 6, katlar),
                new("totalFloors",  "Kat Sayısı",        Domain.Enums.AttributeDataType.Int,    false, 7, []),
                new("heating",      "Isıtma",            Domain.Enums.AttributeDataType.Enum,   false, 8, isiitmaTipleri),
                new("bathCount",    "Banyo Sayısı",      Domain.Enums.AttributeDataType.Enum,   false, 9, banyoSayilari),
                new("kitchen",      "Mutfak",            Domain.Enums.AttributeDataType.Enum,   false, 10, mutfakTipleri),
                new("balcony",      "Balkon",            Domain.Enums.AttributeDataType.Bool,   false, 11, []),
                new("elevator",     "Asansör",           Domain.Enums.AttributeDataType.Bool,   false, 12, []),
                new("parking",      "Otopark",           Domain.Enums.AttributeDataType.Enum,   false, 13, otoparkTipleri),
                new("furnished",    "Eşyalı",            Domain.Enums.AttributeDataType.Bool,   false, 14, []),
                new("usageStatus",  "Kullanım Durumu",   Domain.Enums.AttributeDataType.Enum,   false, 15, kullanımDurumu),
                new("facade",       "Cephe",             Domain.Enums.AttributeDataType.Enum,   false, 16, cepheOpts),
                new("inSite",       "Site İçerisinde",   Domain.Enums.AttributeDataType.Bool,   false, 17, []),
                new("siteName",     "Site Adı",          Domain.Enums.AttributeDataType.String, false, 18, []),
                new("dues",         "Aidat (TL)",        Domain.Enums.AttributeDataType.Int,    false, 19, []),
                new("mortgageable", "Krediye Uygun",     Domain.Enums.AttributeDataType.Bool,   false, 20, []),
                new("deedStatus",   "Tapu Durumu",       Domain.Enums.AttributeDataType.Enum,   false, 21, tapuDurumu),
                new("seller",       "Kimden",            Domain.Enums.AttributeDataType.Enum,   false, 22, kimden),
                new("tradeIn",      "Takas",             Domain.Enums.AttributeDataType.Bool,   false, 23, []),
            },

            // ── İş Yeri ─────────────────────────────────────────────────────
            "is-yeri" => new List<AttrDef>
            {
                new("listingType",    "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("propertyType",   "İş Yeri Tipi",      Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("ofis","Ofis",(string?)null),("dukkan","Dükkan / Mağaza",null),
                    ("depo","Depo / Antrepo",null),("fabrika","Fabrika / Atölye",null),
                    ("is-hani","İş Hanı",null),("plaza","Plaza",null),
                    ("akaryakit","Akaryakıt İstasyonu",null),("diger","Diğer",null))),
                new("grossSqm",       "m² (Brüt)",         Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("netSqm",         "m² (Net)",          Domain.Enums.AttributeDataType.Int,    false, 3, []),
                new("buildingAge",    "Bina Yaşı",         Domain.Enums.AttributeDataType.Enum,   false, 4, binaYaslari),
                new("floor",          "Bulunduğu Kat",     Domain.Enums.AttributeDataType.Enum,   false, 5, katlar),
                new("totalFloors",    "Kat Sayısı",        Domain.Enums.AttributeDataType.Int,    false, 6, []),
                new("heating",        "Isıtma",            Domain.Enums.AttributeDataType.Enum,   false, 7, isiitmaTipleri),
                new("ceilingHeight",  "Tavan Yüksekliği (m)", Domain.Enums.AttributeDataType.Decimal, false, 8, []),
                new("shopFront",      "Caddeye Cepheli",   Domain.Enums.AttributeDataType.Bool,   false, 9, []),
                new("displayWindow",  "Vitrin",            Domain.Enums.AttributeDataType.Bool,   false, 10, []),
                new("elevator",       "Asansör",           Domain.Enums.AttributeDataType.Bool,   false, 11, []),
                new("parking",        "Otopark",           Domain.Enums.AttributeDataType.Enum,   false, 12, otoparkTipleri),
                new("furnished",      "Eşyalı",            Domain.Enums.AttributeDataType.Bool,   false, 13, []),
                new("usageStatus",    "Kullanım Durumu",   Domain.Enums.AttributeDataType.Enum,   false, 14, kullanımDurumu),
                new("inSite",         "Site / Plaza İçi",  Domain.Enums.AttributeDataType.Bool,   false, 15, []),
                new("dues",           "Aidat (TL)",        Domain.Enums.AttributeDataType.Int,    false, 16, []),
                new("mortgageable",   "Krediye Uygun",     Domain.Enums.AttributeDataType.Bool,   false, 17, []),
                new("deedStatus",     "Tapu Durumu",       Domain.Enums.AttributeDataType.Enum,   false, 18, tapuDurumu),
                new("seller",         "Kimden",            Domain.Enums.AttributeDataType.Enum,   false, 19, kimden),
                new("tradeIn",        "Takas",             Domain.Enums.AttributeDataType.Bool,   false, 20, []),
            },

            // ── Arsa ────────────────────────────────────────────────────────
            "arsa" => new List<AttrDef>
            {
                new("listingType",  "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("landType",     "Arsa Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("konut-arsasi","Konut Arsası",(string?)null),("ticari-arsa","Ticari Arsa",null),
                    ("sanayi-arsasi","Sanayi Arsası",null),("tarla","Tarla",null),
                    ("bahce","Bahçe",null),("zeytinlik","Zeytinlik",null),
                    ("bag","Bağ",null),("orman","Orman / Fundalık",null),("diger","Diğer",null))),
                new("grossSqm",     "Alan (m²)",         Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("zoningStatus", "İmar Durumu",       Domain.Enums.AttributeDataType.Enum,   false, 3, Opts(
                    ("imarsiz","İmarsız",(string?)null),("konut-imari","Konut İmarlı",null),
                    ("ticari-imar","Ticari İmarlı",null),("sanayi-imari","Sanayi İmarlı",null),
                    ("tarim","Tarım Arazisi",null),("turizm","Turizm İmarlı",null),("diger","Diğer",null))),
                new("kaks",         "KAKS / Emsal",      Domain.Enums.AttributeDataType.Decimal, false, 4, []),
                new("gabariHeight", "Gabari (kat)",      Domain.Enums.AttributeDataType.Int,    false, 5, []),
                new("adaParsel",    "Ada / Parsel",      Domain.Enums.AttributeDataType.String, false, 6, []),
                new("roadAccess",   "Yola Cepheli",      Domain.Enums.AttributeDataType.Bool,   false, 7, []),
                new("deedStatus",   "Tapu Durumu",       Domain.Enums.AttributeDataType.Enum,   false, 8, tapuDurumu),
                new("mortgageable", "Krediye Uygun",     Domain.Enums.AttributeDataType.Bool,   false, 9, []),
                new("seller",       "Kimden",            Domain.Enums.AttributeDataType.Enum,   false, 10, kimden),
                new("tradeIn",      "Takas",             Domain.Enums.AttributeDataType.Bool,   false, 11, []),
            },

            // ── Konut Projeleri ─────────────────────────────────────────────
            "konut-projeleri" => new List<AttrDef>
            {
                new("listingType",     "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("propertyType",    "Konut Tipi",       Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("daire","Daire",(string?)null),("villa","Villa",null),("mustakil-ev","Müstakil Ev",null),
                    ("residence","Residence",null),("diger","Diğer",null))),
                new("grossSqm",        "m² (Brüt)",        Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("netSqm",          "m² (Net)",         Domain.Enums.AttributeDataType.Int,    false, 3, []),
                new("roomCount",       "Oda Sayısı",       Domain.Enums.AttributeDataType.Enum,   false, 4, odaSayilari),
                new("floor",           "Bulunduğu Kat",    Domain.Enums.AttributeDataType.Enum,   false, 5, katlar),
                new("totalFloors",     "Kat Sayısı",       Domain.Enums.AttributeDataType.Int,    false, 6, []),
                new("projectStatus",   "Proje Durumu",     Domain.Enums.AttributeDataType.Enum,   false, 7, Opts(
                    ("on-satıs","Ön Satış",(string?)null),("insaat-halinde","İnşaat Halinde",null),
                    ("teslim-edildi","Teslim Edildi",null))),
                new("deliveryDate",    "Tahmini Teslim",   Domain.Enums.AttributeDataType.String, false, 8, []),
                new("developerName",   "Proje / Firma Adı", Domain.Enums.AttributeDataType.String, false, 9, []),
                new("heating",         "Isıtma",           Domain.Enums.AttributeDataType.Enum,   false, 10, isiitmaTipleri),
                new("elevator",        "Asansör",          Domain.Enums.AttributeDataType.Bool,   false, 11, []),
                new("parking",         "Otopark",          Domain.Enums.AttributeDataType.Enum,   false, 12, otoparkTipleri),
                new("inSite",          "Site İçerisinde",  Domain.Enums.AttributeDataType.Bool,   false, 13, []),
                new("siteName",        "Proje Adı",        Domain.Enums.AttributeDataType.String, false, 14, []),
                new("dues",            "Aidat (TL)",       Domain.Enums.AttributeDataType.Int,    false, 15, []),
                new("mortgageable",    "Krediye Uygun",    Domain.Enums.AttributeDataType.Bool,   false, 16, []),
                new("deedStatus",      "Tapu Durumu",      Domain.Enums.AttributeDataType.Enum,   false, 17, tapuDurumu),
                new("seller",          "Kimden",           Domain.Enums.AttributeDataType.Enum,   false, 18, kimden),
            },

            // ── Bina ────────────────────────────────────────────────────────
            "bina" => new List<AttrDef>
            {
                new("listingType",   "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("buildingType",  "Bina Tipi",          Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("apartman","Apartman",(string?)null),("villa","Villa / Konak",null),
                    ("isyeri-binasi","İş Yeri Binası",null),("karma","Karma Bina",null),("diger","Diğer",null))),
                new("grossSqm",      "Toplam Alan (m²)",   Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("unitCount",     "Daire / Birim Sayısı", Domain.Enums.AttributeDataType.Int,  false, 3, []),
                new("totalFloors",   "Kat Sayısı",         Domain.Enums.AttributeDataType.Int,    false, 4, []),
                new("buildingAge",   "Bina Yaşı",          Domain.Enums.AttributeDataType.Enum,   false, 5, binaYaslari),
                new("heating",       "Isıtma",             Domain.Enums.AttributeDataType.Enum,   false, 6, isiitmaTipleri),
                new("elevator",      "Asansör",            Domain.Enums.AttributeDataType.Bool,   false, 7, []),
                new("parking",       "Otopark",            Domain.Enums.AttributeDataType.Enum,   false, 8, otoparkTipleri),
                new("usageStatus",   "Kullanım Durumu",    Domain.Enums.AttributeDataType.Enum,   false, 9, kullanımDurumu),
                new("deedStatus",    "Tapu Durumu",        Domain.Enums.AttributeDataType.Enum,   false, 10, tapuDurumu),
                new("mortgageable",  "Krediye Uygun",      Domain.Enums.AttributeDataType.Bool,   false, 11, []),
                new("seller",        "Kimden",             Domain.Enums.AttributeDataType.Enum,   false, 12, kimden),
                new("tradeIn",       "Takas",              Domain.Enums.AttributeDataType.Bool,   false, 13, []),
            },

            // ── Devre Mülk ──────────────────────────────────────────────────
            "devre-mulk" => new List<AttrDef>
            {
                new("listingType",   "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("propertyType",  "Tesis Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("tatil-koyu","Tatil Köyü",(string?)null),("otel-dairesi","Otel Dairesi",null),
                    ("villa","Villa",null),("apart","Apart",null),("diger","Diğer",null))),
                new("grossSqm",      "m² (Brüt)",          Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("roomCount",     "Oda Sayısı",         Domain.Enums.AttributeDataType.Enum,   false, 3, odaSayilari),
                new("period",        "Dönem / Hafta",      Domain.Enums.AttributeDataType.Enum,   false, 4, Opts(
                    ("yaz","Yaz Dönemi",(string?)null),("kis","Kış Dönemi",null),
                    ("ilkbahar","İlkbahar Dönemi",null),("sonbahar","Sonbahar Dönemi",null),("yil-boyu","Yıl Boyu",null))),
                new("weekCount",     "Hafta Sayısı",       Domain.Enums.AttributeDataType.Int,    false, 5, []),
                new("resortName",    "Tesis Adı",          Domain.Enums.AttributeDataType.String, false, 6, []),
                new("location",      "Bölge / Konum",      Domain.Enums.AttributeDataType.String, false, 7, []),
                new("dues",          "Yıllık Aidat (TL)",  Domain.Enums.AttributeDataType.Int,    false, 8, []),
                new("deedStatus",    "Tapu Durumu",        Domain.Enums.AttributeDataType.Enum,   false, 9, tapuDurumu),
                new("seller",        "Kimden",             Domain.Enums.AttributeDataType.Enum,   false, 10, kimden),
                new("tradeIn",       "Takas",              Domain.Enums.AttributeDataType.Bool,   false, 11, []),
            },

            // ── Turistik Tesis ──────────────────────────────────────────────
            "turistik-tesis" => new List<AttrDef>
            {
                new("listingType",   "İlan Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  0, Opts(
                    ("satilik","Satılık",(string?)null),("kiralik","Kiralık",null))),
                new("facilityType",  "Tesis Tipi",         Domain.Enums.AttributeDataType.Enum,   true,  1, Opts(
                    ("otel","Otel",(string?)null),("pansiyon","Pansiyon / Butik Otel",null),
                    ("tatil-koyu","Tatil Köyü",null),("villa-kompleks","Villa Kompleksi",null),
                    ("kamp-alani","Kamp Alanı",null),("restaurant","Restaurant / Kafe",null),
                    ("diger","Diğer",null))),
                new("grossSqm",      "Toplam Alan (m²)",   Domain.Enums.AttributeDataType.Int,    false, 2, []),
                new("roomCount",     "Oda Sayısı",         Domain.Enums.AttributeDataType.Int,    false, 3, []),
                new("bedCount",      "Yatak Kapasitesi",   Domain.Enums.AttributeDataType.Int,    false, 4, []),
                new("starRating",    "Yıldız / Sınıf",     Domain.Enums.AttributeDataType.Enum,   false, 5, Opts(
                    ("1","1 Yıldız",(string?)null),("2","2 Yıldız",null),("3","3 Yıldız",null),
                    ("4","4 Yıldız",null),("5","5 Yıldız",null),("yildızsiz","Yıldızsız",null))),
                new("licenseStatus", "İşletme Ruhsatı",    Domain.Enums.AttributeDataType.Bool,   false, 6, []),
                new("buildingAge",   "Bina Yaşı",          Domain.Enums.AttributeDataType.Enum,   false, 7, binaYaslari),
                new("usageStatus",   "Kullanım Durumu",    Domain.Enums.AttributeDataType.Enum,   false, 8, kullanımDurumu),
                new("deedStatus",    "Tapu Durumu",        Domain.Enums.AttributeDataType.Enum,   false, 9, tapuDurumu),
                new("mortgageable",  "Krediye Uygun",      Domain.Enums.AttributeDataType.Bool,   false, 10, []),
                new("seller",        "Kimden",             Domain.Enums.AttributeDataType.Enum,   false, 11, kimden),
                new("tradeIn",       "Takas",              Domain.Enums.AttributeDataType.Bool,   false, 12, []),
            },

            _ => new List<AttrDef>()
        };
    }

    private static string Slugify(string s) =>
        s.ToLowerInvariant()
         .Replace(" ", "-").Replace("ı","i").Replace("ğ","g").Replace("ü","u")
         .Replace("ş","s").Replace("ö","o").Replace("ç","c").Replace("İ","i")
         .Replace("/","-").Replace("&","-").Replace(".","-")
         .Trim('-');
}
