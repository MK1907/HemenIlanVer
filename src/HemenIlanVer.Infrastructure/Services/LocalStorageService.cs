using HemenIlanVer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HemenIlanVer.Infrastructure.Services;

/// <summary>
/// Cloudflare R2 credentials eksik olduğunda devreye giren local disk storage.
/// Dosyaları /app/uploads/ altına yazar; API /uploads/* ile serve eder.
/// </summary>
public sealed class LocalStorageService : IStorageService
{
    private static readonly string UploadsPath =
        Path.Combine(AppContext.BaseDirectory, "uploads");

    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(ILogger<LocalStorageService> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(UploadsPath);
        _logger.LogWarning("Cloudflare R2 credentials eksik — LocalStorageService aktif (geliştirme modu). Dosyalar: {Path}", UploadsPath);
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        var filePath = Path.Combine(UploadsPath, key);

        await using var fs = File.Create(filePath);
        await stream.CopyToAsync(fs, ct);

        return $"/uploads/{key}";
    }
}
