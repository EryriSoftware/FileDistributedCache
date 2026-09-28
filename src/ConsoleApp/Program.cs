using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

// Arrange
using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .BuildServiceProvider();

var cache = services.GetRequiredService<IFileDistributedCache>();
var key = Guid.NewGuid().ToString("N");
var payload = Guid.NewGuid().ToString("N");

// Act
await cache.SetStringAsync(key, payload);
var actual = await cache.GetStringAsync(key);

// Assert
if (actual != payload)
{
    throw new Exception("FileDistributedCache write value is not the same as the read value");
}