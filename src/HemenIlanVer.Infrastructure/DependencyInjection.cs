using System.Text;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Infrastructure.Identity;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using HemenIlanVer.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HemenIlanVer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName));

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IAiCategoryBootstrapService, AiCategoryBootstrapService>();
        services.AddScoped<IListingService, ListingService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IAiListingExtractionService, AiListingExtractionService>();
        services.AddScoped<IAiListingPartialSuggestionService, AiListingPartialSuggestionService>();
        services.AddScoped<IAiSearchExtractionService, AiSearchExtractionService>();
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IListingIndexService, ListingIndexService>();
        services.AddScoped<IRagSearchService, RagSearchService>();
        services.AddSingleton<IStorageService, CloudflareR2StorageService>();

        // Kategori zenginleştirme kuyruğu + arka plan işçisi
        services.AddSingleton<ICategoryEnrichmentQueue, CategoryEnrichmentQueue>();
        services.AddHostedService<CategoryEnrichmentWorker>();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt ayarları eksik.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
            };
        });

        services.AddAuthorization();
        return services;
    }
}
