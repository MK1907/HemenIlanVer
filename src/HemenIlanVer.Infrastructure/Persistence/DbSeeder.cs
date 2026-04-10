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
}
