using BenchmarkDotNet.Attributes;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Eryri_FileDistributedCache_Benchmarks : Benchmarks
{
    [ParamsAllValues]
    public bool RunAtCapacity;

    public override async Task Setup()
    {
        Services.AddDistributedFileCache(d =>
        {
            d.SizeLimitBytes = RunAtCapacity ? InitialSize : null;
            d.CacheDirectory = CacheDirectory.FullName;
        });
        await base.Setup();
    }
}
