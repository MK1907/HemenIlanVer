namespace HemenIlanVer.Application.Abstractions;

public interface IStorageService
{
    /// <summary>
    /// Dosyayı storage'a yükler ve public erişim URL'ini döner.
    /// </summary>
    Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default);
}
