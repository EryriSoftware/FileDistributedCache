using System.Diagnostics;
using System.Security.Cryptography;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class ReadWriteTests : PerformanceTestsBase
{
    private const string ForbiddenFilenameCharacters = "<>:\"/\\|?*";
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    [TestCase(4 << 10)] // 4 KiB
    [TestCase(64 << 10)] // 64 KiB
    public void Can_Set_And_Get(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var writeTicks = new long[EntriesCount];
        var readTicks = new long[EntriesCount];
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        // Act
        for (int i = 0; i < writeTicks.Length; i++)
        {
            var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
            long start = Stopwatch.GetTimestamp();

            ctx.Cache.Set(key, payload, options);

            writeTicks[i] = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();

            var actual = ctx.Cache.Get(key);

            readTicks[i] = Stopwatch.GetTimestamp() - start;
            actual.Should().BeEquivalentTo(payload);
        }

        // Assert
        Console.WriteLine("Write performance");
        PrintLatency(writeTicks);

        Console.WriteLine("Read performance");
        PrintLatency(readTicks);
    }

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    [TestCase(4 << 10)] // 4 KiB
    [TestCase(64 << 10)] // 64 KiB
    public async Task Can_SetAsync_And_GetAsync(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var writeTicks = new long[EntriesCount];
        var readTicks = new long[EntriesCount];
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        // Act
        for (int i = 0; i < writeTicks.Length; i++)
        {
            var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
            long start = Stopwatch.GetTimestamp();

            await ctx.Cache.SetAsync(key, payload, options, CancellationToken);

            writeTicks[i] = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();

            var actual = await ctx.Cache.GetAsync(key, CancellationToken);

            readTicks[i] = Stopwatch.GetTimestamp() - start;
            actual.Should().BeEquivalentTo(payload);
        }

        // Assert
        Console.WriteLine("Write performance");
        PrintLatency(writeTicks);

        Console.WriteLine("Read performance");
        PrintLatency(readTicks);
    }
}
