namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class LiteDb_Extensions_Caching_Benchmarks : Benchmarks
{
    public LiteDb_Extensions_Caching_Benchmarks()
    {
        LiteDb.Extensions.Caching.ServiceCollectionExtensions.AddLiteDbCache(services, d =>
        {
            d.CachePath = Path.Join(cacheDirectory.FullName, "cache.db");
        });
    }
}
