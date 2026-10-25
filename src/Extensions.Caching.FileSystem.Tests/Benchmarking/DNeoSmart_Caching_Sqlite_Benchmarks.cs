namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class DNeoSmart_Caching_Sqlite_Benchmarks : Benchmarks
{
    public override async Task Setup()
    {
        NeoSmart.Caching.Sqlite.AspSqliteCacheServiceCollectionExtensions.AddSqliteCache(Services, d =>
        {
            d.CachePath = Path.Join(CacheDirectory.FullName, "cache.db");
        });
        await base.Setup();
    }

    protected override void Dispose(bool isDisposing)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        base.Dispose(isDisposing);
    }
}