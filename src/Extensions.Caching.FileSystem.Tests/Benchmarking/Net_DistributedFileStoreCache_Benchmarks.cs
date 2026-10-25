using BenchmarkDotNet.Attributes;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Net_DistributedFileStoreCache_Benchmarks : Benchmarks
{
    [ParamsAllValues]
    public bool RunAtCapacity;

    public Net_DistributedFileStoreCache_Benchmarks()
    {
        Net.DistributedFileStoreCache.RegisterDistributedFileStoreCache.AddDistributedFileStoreCache(services, d =>
        {
            d.PathToCacheFileDirectory = cacheDirectory.FullName;
            d.SecondPartOfCacheFileName = "cache";
            d.MaxBytesInJsonCacheFile = RunAtCapacity ? InitialSize : 10000;
            d.WhichVersion = Net.DistributedFileStoreCache.FileStoreCacheVersions.IDistributedCache;
        });
    }
}
