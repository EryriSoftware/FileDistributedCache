using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

// Arrange
using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .AddHybridCache().Services
    .BuildServiceProvider();

var cache = services.GetRequiredService<HybridCache>();
var key = Guid.NewGuid().ToString("N");
var payload = Guid.NewGuid().ToString("N");
var options = new HybridCacheEntryOptions
{
    Flags = HybridCacheEntryFlags.DisableLocalCache
};

// Act
await cache.SetAsync(key, payload, options);
var actual = await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(string.Empty), options);

// Assert
if (actual != payload)
{
    throw new Exception("FileDistributedCache write value is not the same as the read value");
}
else
{
    Console.WriteLine("FileDistributedCache can write and read");
}