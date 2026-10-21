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
var payload = "FileDistributedCache can write and read";
var options = new HybridCacheEntryOptions
{
    Expiration = TimeSpan.FromMinutes(10),
    LocalCacheExpiration = TimeSpan.FromSeconds(5),
    Flags = HybridCacheEntryFlags.DisableLocalCache // Disables the 'IMemoryCache' for the purpose of thie example
};

// Act
await cache.SetAsync(key, payload, options);
var actual = await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("Cache miss"), options);

// Assert
Console.WriteLine(actual);
if (actual != payload)
{
    throw new Exception("FileDistributedCache write value is not the same as the read value");
}