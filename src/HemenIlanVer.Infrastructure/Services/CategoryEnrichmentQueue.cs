using System.Threading.Channels;
using HemenIlanVer.Application.Abstractions;

namespace HemenIlanVer.Infrastructure.Services;

/// <summary>
/// System.Threading.Channels tabanlı bellek içi kuyruk.
/// Singleton olarak kaydedilir; aynı anda birden fazla worker okuyabilir.
/// </summary>
public sealed class CategoryEnrichmentQueue : ICategoryEnrichmentQueue
{
    private readonly Channel<CategoryEnrichmentJob> _channel =
        Channel.CreateBounded<CategoryEnrichmentJob>(
            new BoundedChannelOptions(500)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

    public void Enqueue(CategoryEnrichmentJob job)
        => _channel.Writer.TryWrite(job);

    public ValueTask<CategoryEnrichmentJob> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);
}
