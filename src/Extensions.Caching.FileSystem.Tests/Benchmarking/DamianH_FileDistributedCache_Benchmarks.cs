using Microsoft.Extensions.DependencyInjection;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class DamianH_FileDistributedCache_Benchmarks : Benchmarks
{
    public DamianH_FileDistributedCache_Benchmarks()
    {
        FileDistributedCacheServiceCollectionExtensions.AddFileDistributedCache(services, d =>
        {
            d.CacheDirectory = cacheDirectory.FullName;
            d.MaxTotalSize = null;
        });
    }
}