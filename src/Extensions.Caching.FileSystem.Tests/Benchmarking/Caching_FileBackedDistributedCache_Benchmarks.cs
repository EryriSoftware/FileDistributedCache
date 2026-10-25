namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class Caching_FileBackedDistributedCache_Benchmarks : Benchmarks
{
    public Caching_FileBackedDistributedCache_Benchmarks()
    {
        FileBackedCache.Extensions.AddFileBackedCache(services, cacheDirectory.FullName);
    }
}
