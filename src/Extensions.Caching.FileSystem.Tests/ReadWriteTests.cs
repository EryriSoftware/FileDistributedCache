using System.Security.Cryptography;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class ReadWriteTests
{
    private const string ForbiddenFilenameCharacters = "<>:\"/\\|?*";
    private Fixture fixture = new Fixture();
    private byte[] Bytes(int length = 100) => RandomNumberGenerator.GetBytes(length);
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public void Can_Set_And_Get(int payloadSize)
    {
        using var ctx = new CacheContext();
        var items = Enumerable
            .Repeat(payloadSize, 100)
            .ToDictionary(d => fixture.Create<string>() + ForbiddenFilenameCharacters, Bytes)
            .ToArray();
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        Parallel.ForEach(items, item =>
        {
            ctx.Cache.Set(item.Key, item.Value, options);
            ctx.Cache.Get(item.Key).Should().BeEquivalentTo(item.Value);
        });
    }

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public async Task Can_SetAsync_And_GetAsync(int payloadSize)
    {
        using var ctx = new CacheContext();
        var items = Enumerable
            .Repeat(payloadSize, 100)
            .ToDictionary(d => fixture.Create<string>() + ForbiddenFilenameCharacters, Bytes)
            .ToArray();
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        await Parallel.ForEachAsync(items, CancellationToken, async (item, ct) =>
        {
            await ctx.Cache.SetAsync(item.Key, item.Value, options, ct);
            var value = await ctx.Cache.GetAsync(item.Key, ct);
            value.Should().BeEquivalentTo(item.Value);
        });
    }
}
