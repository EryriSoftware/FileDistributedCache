using Microsoft.Extensions.DependencyInjection;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class DamianH_FileDistributedCache_Benchmarks : Benchmarks
{
    public override async Task Setup()
    {
        FileDistributedCacheServiceCollectionExtensions.AddFileDistributedCache(Services, d =>
        {
            d.CacheDirectory = CacheDirectory.FullName;
            d.MaxTotalSize = null;
        });
        await base.Setup();
    }
}