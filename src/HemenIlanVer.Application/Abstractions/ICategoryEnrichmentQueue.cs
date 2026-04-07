namespace HemenIlanVer.Application.Abstractions;

/// <summary>
/// AI ile tespit edilen kategori + attribute değerleriyle zenginleştirme işi.
/// DetectedValues varsa worker önce o değerlere öncelik tanır (örn. seçilen marka ilk işlenir).
/// </summary>
public record CategoryEnrichmentJob(
    Guid CategoryId,
    IReadOnlyDictionary<string, string>? DetectedValues = null);

/// <summary>
/// Kategori tespit edilince arka planda Enum attribute option değerlerini
/// AI ile doldurmak için kullanılan kuyruk.
/// </summary>
public interface ICategoryEnrichmentQueue
{
    /// <summary>Kuyruğa yeni bir zenginleştirme işi ekler (non-blocking).</summary>
    void Enqueue(CategoryEnrichmentJob job);

    /// <summary>Worker tarafından okunur; iş gelene kadar bekler.</summary>
    ValueTask<CategoryEnrichmentJob> DequeueAsync(CancellationToken ct);
}
