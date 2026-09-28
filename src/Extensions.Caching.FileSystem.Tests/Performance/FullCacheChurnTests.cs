using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture, Parallelizable(ParallelScope.All)]
public class FullCacheChurnTests : PerformanceTestsBase
{
    protected const int PayloadSize = 1;
    protected const int CacheSizeLimit = PayloadSize * 10;
    protected readonly byte[] Payload = RandomNumberGenerator.GetBytes(PayloadSize);

    [Test]
    public void Set_Has_No_Conficts([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var ticks = new long[EntriesCount];
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.For(0, 10, i =>
        {
            ctx.Cache.Set(Guid.NewGuid().ToString("N"), Payload, options);
        });

        ctx.Cache.Size.Should().Be(CacheSizeLimit);

        // Act
        for (int i = 0; i < ticks.Length; i++)
        {
            var key = Guid.NewGuid().ToString("N");
            long start = Stopwatch.GetTimestamp();

            ctx.Cache.Set(key, Payload, options);

            ticks[i] = Stopwatch.GetTimestamp() - start;
        }

        // Assert
        PrintLatency(ticks);
    }

    [Test]
    public async Task SetAsync_Has_No_Conficts([Values] EvictionPolicy evictionPolicy)
    {
        // Arrange
        using var ctx = new CacheContext(evictionPolicy: evictionPolicy, sizeLimitBytes: CacheSizeLimit);
        var ticks = new long[EntriesCount];
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.For(0, 10, i =>
        {
            ctx.Cache.Set(Guid.NewGuid().ToString("N"), Payload, options);
        });

        ctx.Cache.Size.Should().Be(CacheSizeLimit);

        // Act
        for (int i = 0; i < ticks.Length; i++)
        {
            var key = Guid.NewGuid().ToString("N");
            long start = Stopwatch.GetTimestamp();

            await ctx.Cache.SetAsync(key, Payload, options, TestContext.CurrentContext.CancellationToken);

            ticks[i] = Stopwatch.GetTimestamp() - start;
        }

        // Assert
        PrintLatency(ticks);
    }
}
