using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
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
            db.Cities.Add(new City { Id = cityId, Name = "İstanbul", Slug = "istanbul", PlateCode = 34, CreatedAt = DateTimeOffset.UtcNow });
            db.Districts.Add(new District { Id = distId, CityId = cityId, Name = "Ataşehir", Slug = "atasehir", CreatedAt = DateTimeOffset.UtcNow });

            var catArac = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0001");
            var catOtomobil = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0002");
            db.Categories.Add(new Category
            {
                Id = catArac,
                ParentId = null,
                Name = "Araç",
                Slug = "arac",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Categories.Add(new Category
            {
                Id = catOtomobil,
                ParentId = catArac,
                Name = "Otomobil",
                Slug = "otomobil",
                SortOrder = 1,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

            void AddOtomobilAttr(Guid id, string key, string display, AttributeDataType dt, bool req, int order, params (string vk, string lbl)[] opts)
            {
                var a = new CategoryAttribute
                {
                    Id = id,
                    CategoryId = catOtomobil,
                    AttributeKey = key,
                    DisplayName = display,
                    DataType = dt,
                    IsRequired = req,
                    SortOrder = order,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.CategoryAttributes.Add(a);
                var i = 0;
                foreach (var (vk, lbl) in opts)
                {
                    db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                    {
                        Id = Guid.NewGuid(),
                        CategoryAttributeId = id,
                        ValueKey = vk,
                        Label = lbl,
                        SortOrder = ++i,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0001"), "brand", "Marka", AttributeDataType.String, true, 1);
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0002"), "model", "Model", AttributeDataType.String, true, 2);
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0003"), "year", "Yıl", AttributeDataType.Int, true, 3);
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0004"), "km", "Kilometre", AttributeDataType.Int, false, 4);
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0005"), "gear", "Vites", AttributeDataType.Enum, false, 5,
                ("Manuel", "Manuel"), ("Otomatik", "Otomatik"));
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0006"), "fuel", "Yakıt", AttributeDataType.Enum, false, 6,
                ("Benzin", "Benzin"), ("Dizel", "Dizel"), ("Hibrit", "Hibrit"), ("Elektrik", "Elektrik"));
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0007"), "bodyType", "Kasa tipi", AttributeDataType.Enum, false, 7,
                ("Sedan", "Sedan"), ("Hatchback", "Hatchback"), ("SUV", "SUV"));
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0008"), "damage", "Hasar durumu", AttributeDataType.Enum, false, 8,
                ("Degisensiz", "Değişensiz"), ("AzHasarli", "Az hasarlı"));
            AddOtomobilAttr(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0009"), "color", "Renk", AttributeDataType.String, false, 9);

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

            var listingId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeee0001");
            db.Listings.Add(new Listing
            {
                Id = listingId,
                UserId = demo.Id,
                CategoryId = catOtomobil,
                Title = "2021 Fiat Egea Sedan Otomatik",
                Description = "32.000 km, değişensiz, İstanbul Ataşehir.",
                Price = 950_000,
                Currency = "TRY",
                Status = ListingStatus.Published,
                ListingType = ListingType.Satilik,
                CityId = cityId,
                DistrictId = distId,
                PublishedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                ViewCount = 12
            });

            db.ListingAttributeValues.AddRange(
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0001"), ValueText = "Fiat", CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0002"), ValueText = "Egea", CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0003"), ValueInt = 2021, CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0004"), ValueInt = 32000, CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0005"), ValueText = "Otomatik", CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0007"), ValueText = "Sedan", CreatedAt = DateTimeOffset.UtcNow },
                new ListingAttributeValue { Id = Guid.NewGuid(), ListingId = listingId, CategoryAttributeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0008"), ValueText = "Degisensiz", CreatedAt = DateTimeOffset.UtcNow }
            );

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Veritabanı ilk tohum verisi yüklendi.");
        }

        await EnsureExtendedCategoriesAsync(db, ct);
        await EnsureEducationCategoryAsync(db, ct);
        await EnsurePetCategoryAsync(db, ct);
    }

    /// <summary>
    /// Mevcut veritabanlarına yeni ana/alt kategorileri ekler (idempotent).
    /// </summary>
    private static async Task EnsureExtendedCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.Slug == "emlak", ct))
            return;

        var catEmlak = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0003");
        var catElektronik = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0004");
        var catKonut = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0010");
        var catIsyeri = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0011");
        var catTelefon = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0012");
        var catLaptop = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0013");

        db.Categories.AddRange(
            new Category
            {
                Id = catEmlak,
                ParentId = null,
                Name = "Emlak",
                Slug = "emlak",
                SortOrder = 2,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catElektronik,
                ParentId = null,
                Name = "Elektronik",
                Slug = "elektronik",
                SortOrder = 3,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catKonut,
                ParentId = catEmlak,
                Name = "Konut",
                Slug = "konut",
                SortOrder = 1,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catIsyeri,
                ParentId = catEmlak,
                Name = "İşyeri",
                Slug = "isyeri",
                SortOrder = 2,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catTelefon,
                ParentId = catElektronik,
                Name = "Cep Telefonu",
                Slug = "telefon",
                SortOrder = 1,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catLaptop,
                ParentId = catElektronik,
                Name = "Laptop",
                Slug = "laptop",
                SortOrder = 2,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

        void AddAttr(Guid catId, Guid attrId, string key, string display, AttributeDataType dt, bool req, int order, params (string vk, string lbl)[] opts)
        {
            db.CategoryAttributes.Add(new CategoryAttribute
            {
                Id = attrId,
                CategoryId = catId,
                AttributeKey = key,
                DisplayName = display,
                DataType = dt,
                IsRequired = req,
                SortOrder = order,
                CreatedAt = DateTimeOffset.UtcNow
            });
            var i = 0;
            foreach (var (vk, lbl) in opts)
            {
                db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                {
                    Id = Guid.NewGuid(),
                    CategoryAttributeId = attrId,
                    ValueKey = vk,
                    Label = lbl,
                    SortOrder = ++i,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        AddAttr(catKonut, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0201"), "roomCount", "Oda sayısı", AttributeDataType.String, true, 1);
        AddAttr(catKonut, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0202"), "m2", "Metrekare", AttributeDataType.Decimal, true, 2);
        AddAttr(catKonut, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0203"), "floor", "Bulunduğu kat", AttributeDataType.Int, false, 3);
        AddAttr(catKonut, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0204"), "heating", "Isıtma", AttributeDataType.Enum, false, 4,
            ("Kombi", "Kombi"), ("Merkezi", "Merkezi"), ("Yok", "Yok"));

        AddAttr(catIsyeri, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0205"), "m2", "Metrekare", AttributeDataType.Decimal, true, 1);
        AddAttr(catIsyeri, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0206"), "usage", "Kullanım", AttributeDataType.Enum, true, 2,
            ("Ofis", "Ofis"), ("Magaza", "Mağaza"), ("Depo", "Depo"));

        AddAttr(catTelefon, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0211"), "brand", "Marka", AttributeDataType.String, true, 1);
        AddAttr(catTelefon, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0212"), "model", "Model", AttributeDataType.String, true, 2);
        AddAttr(catTelefon, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0213"), "storage", "Hafıza", AttributeDataType.Enum, false, 3,
            ("64", "64 GB"), ("128", "128 GB"), ("256", "256 GB"), ("512", "512 GB"));
        AddAttr(catTelefon, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0214"), "condition", "Durum", AttributeDataType.Enum, false, 4,
            ("Sifir", "Sıfır"), ("IkinciEl", "İkinci el"));

        AddAttr(catLaptop, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0221"), "brand", "Marka", AttributeDataType.String, true, 1);
        AddAttr(catLaptop, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0222"), "ram", "RAM (GB)", AttributeDataType.Int, false, 2);
        AddAttr(catLaptop, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0223"), "disk", "Disk", AttributeDataType.String, false, 3);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Eğitim / özel ders kategorisi (idempotent).
    /// </summary>
    private static async Task EnsureEducationCategoryAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.Slug == "egitim", ct))
            return;

        var catEgitim = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0005");
        var catOzelDers = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0014");

        db.Categories.AddRange(
            new Category
            {
                Id = catEgitim,
                ParentId = null,
                Name = "Eğitim",
                Slug = "egitim",
                SortOrder = 4,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catOzelDers,
                ParentId = catEgitim,
                Name = "Özel Ders",
                Slug = "ozel-ders",
                SortOrder = 1,
                DefaultListingType = ListingType.HizmetVeriyor,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

        void AddAttr(Guid catId, Guid attrId, string key, string display, AttributeDataType dt, bool req, int order, params (string vk, string lbl)[] opts)
        {
            db.CategoryAttributes.Add(new CategoryAttribute
            {
                Id = attrId,
                CategoryId = catId,
                AttributeKey = key,
                DisplayName = display,
                DataType = dt,
                IsRequired = req,
                SortOrder = order,
                CreatedAt = DateTimeOffset.UtcNow
            });
            var i = 0;
            foreach (var (vk, lbl) in opts)
            {
                db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                {
                    Id = Guid.NewGuid(),
                    CategoryAttributeId = attrId,
                    ValueKey = vk,
                    Label = lbl,
                    SortOrder = ++i,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        AddAttr(catOzelDers, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0230"), "subject", "Branş / konu", AttributeDataType.String, true, 1);
        AddAttr(catOzelDers, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0231"), "examTarget", "Hedef sınav", AttributeDataType.Enum, false, 2,
            ("LGS", "LGS"), ("YKS", "YKS"), ("LgsVeYks", "LGS & YKS"), ("Genel", "Genel / diğer"));
        AddAttr(catOzelDers, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0232"), "teachingFormat", "Ders formatı", AttributeDataType.Enum, false, 3,
            ("Online", "Online"), ("Yuzyuze", "Yüz yüze"), ("Karisik", "Karma"));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Evcil hayvan / sahiplendirme (idempotent).
    /// </summary>
    private static async Task EnsurePetCategoryAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.Slug == "evcil-hayvan", ct))
            return;

        var catEvcil = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0015");
        var catSahiplendirme = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0016");

        db.Categories.AddRange(
            new Category
            {
                Id = catEvcil,
                ParentId = null,
                Name = "Evcil Hayvanlar",
                Slug = "evcil-hayvan",
                SortOrder = 5,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Category
            {
                Id = catSahiplendirme,
                ParentId = catEvcil,
                Name = "Sahiplendirme",
                Slug = "sahiplendirme",
                SortOrder = 1,
                DefaultListingType = ListingType.Satilik,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

        void AddAttr(Guid catId, Guid attrId, string key, string display, AttributeDataType dt, bool req, int order, params (string vk, string lbl)[] opts)
        {
            db.CategoryAttributes.Add(new CategoryAttribute
            {
                Id = attrId,
                CategoryId = catId,
                AttributeKey = key,
                DisplayName = display,
                DataType = dt,
                IsRequired = req,
                SortOrder = order,
                CreatedAt = DateTimeOffset.UtcNow
            });
            var i = 0;
            foreach (var (vk, lbl) in opts)
            {
                db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                {
                    Id = Guid.NewGuid(),
                    CategoryAttributeId = attrId,
                    ValueKey = vk,
                    Label = lbl,
                    SortOrder = ++i,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        AddAttr(catSahiplendirme, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0240"), "species", "Tür", AttributeDataType.Enum, true, 1,
            ("Kedi", "Kedi"), ("Kopek", "Köpek"), ("Diger", "Diğer"));
        AddAttr(catSahiplendirme, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0241"), "gender", "Cinsiyet", AttributeDataType.Enum, false, 2,
            ("Erkek", "Erkek"), ("Disi", "Dişi"));
        AddAttr(catSahiplendirme, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccc0242"), "sterilized", "Kısırlaştırılmış", AttributeDataType.Enum, false, 3,
            ("Evet", "Evet"), ("Hayir", "Hayır"), ("Bilinmiyor", "Bilinmiyor"));

        await db.SaveChangesAsync(ct);
    }
}
