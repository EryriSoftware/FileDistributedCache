using System.Buffers;
using System.Security.Cryptography;
using Eryri.Buffers;
using Eryri.Extensions.Caching.FileSystem.Tests.Contexts;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture, Parallelizable(ParallelScope.All)]
public class SoakTests
{
    protected const int EntriesCount = 1000;
    protected const int PayloadSize = 1;
    protected const int CacheSizeLimit = PayloadSize * 10;
    protected readonly byte[] Payload = RandomNumberGenerator.GetBytes(PayloadSize);

    [Test]
    public void Set_Has_No_Conficts([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.For(0, 10, i =>
        {
            ctx.Cache.Set(Guid.NewGuid().ToString("N"), Payload, options);
        });

        // Act
        // Assert
        Parallel.For(0, EntriesCount, i =>
        {
            var key = Guid.NewGuid().ToString("N");
            ctx.Cache.Set(key, Payload, options);
            ctx.BufferCache.Set(key, new ReadOnlySequence<byte>(Payload), options);

            ctx.Cache.Get(key);
            using var buffer = new ArrayPoolBufferWriter<byte>(Payload.Length);
            ctx.BufferCache.TryGet(key, buffer);
        });
    }

    [Test]
    public async Task SetAsync_Has_No_Conficts([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.For(0, 10, i =>
        {
            ctx.Cache.Set(Guid.NewGuid().ToString("N"), Payload, options);
        });

        // Act
        // Assert
        await Parallel.ForAsync(0, EntriesCount, TestContext.CurrentContext.CancellationToken, async (i, ct) =>
        {
            var key = Guid.NewGuid().ToString("N");
            await Task.WhenAll([
                ctx.Cache.SetAsync(key, Payload, options, ct),
                ctx.BufferCache.SetAsync(key, new ReadOnlySequence<byte>(Payload), options, ct).AsTask()
            ]);

            using var buffer = new ArrayPoolBufferWriter<byte>(Payload.Length);
            await Task.WhenAll([
                ctx.Cache.GetAsync(key, ct),
                ctx.BufferCache.TryGetAsync(key, buffer, ct).AsTask()
            ]);
        });
    }
}
