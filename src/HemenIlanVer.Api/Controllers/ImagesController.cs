using HemenIlanVer.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HemenIlanVer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ImagesController : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IStorageService _storage;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(IStorageService storage, ILogger<ImagesController> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    [HttpPost("upload")]
    [Authorize]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Dosya boş." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "Dosya boyutu 10 MB'ı aşamaz." });

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = "Sadece JPEG, PNG, WebP ve GIF formatları desteklenir." });

        try
        {
            await using var stream = file.OpenReadStream();
            var url = await _storage.UploadAsync(stream, file.FileName, file.ContentType, ct);
            _logger.LogInformation("Upload başarılı: {Url}", url);
            return Ok(new { url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload hatası: {FileName} ({ContentType}, {Size} bytes)", file.FileName, file.ContentType, file.Length);
            return StatusCode(500, new { error = $"Upload hatası: {ex.Message}" });
        }
    }
}
