using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Contexts;

internal class CacheContext : IDisposable
{
    private readonly ServiceProvider Services;
    public DateTimeOffset Now => TimeProvider.GetUtcNow();
    public FakeTimeProvider TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    public IDistributedCache Cache => field ??= Services.GetRequiredService<IDistributedCache>();
    public IBufferDistributedCache BufferCache => field ??= Services.GetRequiredService<IBufferDistributedCache>();
    public IFileDistributedCache FileCache => field ??= Services.GetRequiredService<IFileDistributedCache>();

    public CacheContext(EvictionPolicy evictionPolicy = EvictionPolicy.LRU, int? sizeLimitBytes = null, string? cacheDirectory = null) =>
       Services = new ServiceCollection()
            .AddLogging()
            .AddDistributedFileCache(d =>
            {
                d.EvictionPolicy = evictionPolicy;
                d.SizeLimitBytes = sizeLimitBytes;
                d.CacheDirectory = cacheDirectory;
            })
            .AddSingleton<TimeProvider>(TimeProvider)
            .BuildServiceProvider();

    public void Advance(TimeSpan ts) => TimeProvider.Advance(ts);

    public void Dispose() => Services.Dispose();
}
