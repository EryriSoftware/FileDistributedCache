using BenchmarkDotNet.Attributes;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Net_DistributedFileStoreCache_Benchmarks : Benchmarks
{
    [ParamsAllValues]
    public bool RunAtCapacity;

    public override async Task Setup()
    {
        Net.DistributedFileStoreCache.RegisterDistributedFileStoreCache.AddDistributedFileStoreCache(Services, d =>
        {
            d.PathToCacheFileDirectory = CacheDirectory.FullName;
            d.SecondPartOfCacheFileName = "cache";
            d.MaxBytesInJsonCacheFile = RunAtCapacity ? InitialSize : 10000;
            d.WhichVersion = Net.DistributedFileStoreCache.FileStoreCacheVersions.IDistributedCache;
        });
        await base.Setup();
    }
}
