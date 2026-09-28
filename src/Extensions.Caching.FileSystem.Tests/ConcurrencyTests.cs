using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
public class ConcurrencyTests()
{
    private const int EntriesCount = 1_000;
    private const int PayloadSize = 1;
    private const int CacheSizeLimit = PayloadSize * 10;
    private readonly byte[] Payload = RandomNumberGenerator.GetBytes(PayloadSize);

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
        Parallel.For(0, ticks.Length, i =>
        {
            var key = Guid.NewGuid().ToString("N");
            long start = Stopwatch.GetTimestamp();

            ctx.Cache.Set(key, Payload, options);

            ticks[i] = Stopwatch.GetTimestamp() - start;
        });

        // Assert
        Array.Sort(ticks);

        Console.WriteLine($"P01: {MsAt(0.01):F3} ms");
        Console.WriteLine($"P10: {MsAt(0.10):F3} ms");
        Console.WriteLine($"P50: {MsAt(0.50):F3} ms");
        Console.WriteLine($"P95: {MsAt(0.95):F3} ms");
        Console.WriteLine($"P99: {MsAt(0.99):F3} ms");

        double MsAt(double percentile) => ticks[(int)Math.Ceiling(percentile * ticks.Length) - 1] * 1000.0 / Stopwatch.Frequency;
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
        await Parallel.ForAsync(0, ticks.Length, TestContext.CurrentContext.CancellationToken, async (i, ct) =>
        {
            var key = Guid.NewGuid().ToString("N");
            long start = Stopwatch.GetTimestamp();

            await ctx.Cache.SetAsync(key, Payload, options, ct);

            ticks[i] = Stopwatch.GetTimestamp() - start;
        });

        // Assert
        Array.Sort(ticks);

        Console.WriteLine($"P01: {MsAt(0.01):F3} ms");
        Console.WriteLine($"P10: {MsAt(0.10):F3} ms");
        Console.WriteLine($"P50: {MsAt(0.50):F3} ms");
        Console.WriteLine($"P95: {MsAt(0.95):F3} ms");
        Console.WriteLine($"P99: {MsAt(0.99):F3} ms");

        double MsAt(double percentile) => ticks[(int)Math.Ceiling(percentile * ticks.Length) - 1] * 1000.0 / Stopwatch.Frequency;
    }
}
