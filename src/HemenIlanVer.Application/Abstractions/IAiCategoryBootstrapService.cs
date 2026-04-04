using System.Text.Json;

namespace HemenIlanVer.Application.Abstractions;

/// <summary>
/// OpenAI kategori tespit JSON'unda "bootstrap" ile gelen yeni ana/alt kategori ve filtre şemasını DB'ye yazar.
/// </summary>
public interface IAiCategoryBootstrapService
{
    Task ApplyFromDetectDocumentAsync(JsonElement detectDocument, CancellationToken cancellationToken = default);
}
