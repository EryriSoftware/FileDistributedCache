namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Caching_FileBackedDistributedCache_Benchmarks : Benchmarks
{
    public override async Task Setup()
    {
        FileBackedCache.Extensions.AddFileBackedCache(Services, CacheDirectory.FullName);
        await base.Setup();
    }
}
