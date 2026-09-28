using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

internal class CacheContext : IDisposable
{
    private readonly ServiceProvider Services;
    public DateTimeOffset Now => TimeProvider.GetUtcNow();
    public FakeTimeProvider TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    public IFileDistributedCache Cache => field ??= Services.GetRequiredService<IFileDistributedCache>();

    public CacheContext(EvictionPolicy evictionPolicy = EvictionPolicy.LRU, int? sizeLimitBytes = null) =>
       Services = new ServiceCollection()
            .AddLogging()
            .AddDistributedFileCache(d =>
            {
                d.EvictionPolicy = evictionPolicy;
                d.SizeLimitBytes = sizeLimitBytes;
            })
            .AddSingleton<TimeProvider>(TimeProvider)
            .BuildServiceProvider();

    public void Advance(TimeSpan ts) => TimeProvider.Advance(ts);

    public void Dispose()
    {
        Services.Dispose();
    }
}
