using BenchmarkDotNet.Attributes;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Eryri_FileDistributedCache_Benchmarks : Benchmarks
{
    [ParamsAllValues]
    public bool RunAtCapacity;

    public Eryri_FileDistributedCache_Benchmarks()
    {
        services.AddDistributedFileCache(d =>
        {
            d.SizeLimitBytes = RunAtCapacity ? InitialSize : null;
            d.CacheDirectory = cacheDirectory.FullName;
        });
    }
}
