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
    }
}
