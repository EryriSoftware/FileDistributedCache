namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class LiteDb_Extensions_Caching_Benchmarks : Benchmarks
{
    public override async Task Setup()
    {
        LiteDb.Extensions.Caching.ServiceCollectionExtensions.AddLiteDbCache(Services, d =>
        {
            d.CachePath = Path.Join(CacheDirectory.FullName, "cache.db");
        });
        await base.Setup();
    }
}
