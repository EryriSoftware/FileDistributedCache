using System.Buffers;
using System.Security.Cryptography;
using Eryri.Buffers;
using Eryri.Extensions.Caching.FileSystem.Tests.Contexts;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture, Parallelizable(ParallelScope.All)]
public class SoakTests
{
    protected const int EntriesCount = 100;
    protected const int PayloadSize = 10;
    protected const int CacheSizeLimit = PayloadSize * EntriesCount;
    protected readonly byte[] Payload = RandomNumberGenerator.GetBytes(PayloadSize);
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public void Set_Has_No_Conficts([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.For(0, EntriesCount, i =>
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

            ctx.Cache.Get(key).Should().BeEquivalentTo(Payload);
            using var buffer = new ArrayPoolBufferWriter<byte>(Payload.Length);
            ctx.BufferCache.TryGet(key, buffer).Should().BeTrue();
            buffer.WrittenSpan.ToArray().Should().BeEquivalentTo(Payload);
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

        Parallel.For(0, EntriesCount, i =>
        {
            ctx.Cache.Set(Guid.NewGuid().ToString("N"), Payload, options);
        });

        // Act
        // Assert
        await Parallel.ForAsync(0, EntriesCount, CancellationToken, async (i, ct) =>
        {
            var key = Guid.NewGuid().ToString("N");
            await Task.WhenAll([
                ctx.Cache.SetAsync(key, Payload, options, ct),
                ctx.BufferCache.SetAsync(key, new ReadOnlySequence<byte>(Payload), options, ct).AsTask()
            ]);

            async Task ReadTask()
            {
                var actual = await ctx.Cache.GetAsync(key, ct);
                actual.Should().BeEquivalentTo(Payload);
            }

            async Task ReadBufferTask()
            {
                using var buffer = new ArrayPoolBufferWriter<byte>(Payload.Length);
                await ctx.BufferCache.TryGetAsync(key, buffer, ct);
                buffer.WrittenSpan.ToArray().Should().BeEquivalentTo(Payload);
            }

            await Task.WhenAll(ReadTask(), ReadBufferTask());
        });
    }

    [Test]
    public async Task SetGet_Concurrent_Same_Key([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        var key = Guid.NewGuid().ToString("N");
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        await ctx.Cache.SetAsync(key, Payload, options, CancellationToken);

        // Act
        // Assert
        var setTasks = Parallel.ForAsync(0, EntriesCount, CancellationToken, async (i, ct) =>
        {
            await ctx.Cache.SetAsync(key, Payload, options, ct);
        });

        var getTasks = Parallel.ForAsync(0, EntriesCount, CancellationToken, async (i, ct) =>
        {
            var actual = await ctx.Cache.GetAsync(key, ct);
            actual.Should().BeEquivalentTo(Payload);
        });

        await Task.WhenAll(setTasks, getTasks);
    }
}
