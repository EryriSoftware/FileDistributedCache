namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class DNeoSmart_Caching_Sqlite_Benchmarks : Benchmarks
{
    public DNeoSmart_Caching_Sqlite_Benchmarks()
    {
        NeoSmart.Caching.Sqlite.AspSqliteCacheServiceCollectionExtensions.AddSqliteCache(services, d =>
        {
            d.CachePath = Path.Join(cacheDirectory.FullName, "cache.db");
        });
    }

    protected override void Dispose(bool isDisposing)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        base.Dispose(isDisposing);
    }
}