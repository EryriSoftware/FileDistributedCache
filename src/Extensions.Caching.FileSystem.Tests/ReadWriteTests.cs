using System.Buffers;
using System.Security.Cryptography;
using Eryri.Extensions.Caching.FileSystem.Tests.Contexts;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class ReadWriteTests
{
    private const string ForbiddenFilenameCharacters = "<>:\"/\\|?*";
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public void Can_Set_And_Get(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        // Act
        var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
        ctx.Cache.Set(key, payload, options);
        var actual = ctx.Cache.Get(key);

        // Assert
        actual.Should().BeEquivalentTo(payload);
    }

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public void Can_Set_And_Get_Buffered(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };
        var buffer = new ArrayBufferWriter<byte>(payloadSize);

        // Act
        var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
        ctx.BufferCache.Set(key, new ReadOnlySequence<byte>(payload), options);
        var canRead = ctx.BufferCache.TryGet(key, buffer);

        // Assert
        canRead.Should().BeTrue();
        buffer.WrittenSpan.ToArray().Should().BeEquivalentTo(payload);
    }

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public async Task Can_SetAsync_And_GetAsync_Buffered(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };
        var buffer = new ArrayBufferWriter<byte>(payloadSize);

        // Act
        var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
        await ctx.BufferCache.SetAsync(key, new ReadOnlySequence<byte>(payload), options, CancellationToken);
        var canRead = await ctx.BufferCache.TryGetAsync(key, buffer, CancellationToken);

        // Assert
        canRead.Should().BeTrue();
        buffer.WrittenSpan.ToArray().Should().BeEquivalentTo(payload);
    }

    [TestCase(1)]
    [TestCase(1 << 10)] // 1 KiB
    public async Task Can_SetAsync_And_GetAsync(int payloadSize)
    {
        // Arrange
        using var ctx = new CacheContext();
        var payload = RandomNumberGenerator.GetBytes(payloadSize);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        // Act
        var key = Guid.NewGuid().ToString("N") + ForbiddenFilenameCharacters;
        await ctx.Cache.SetAsync(key, payload, options, CancellationToken);
        var actual = await ctx.Cache.GetAsync(key, CancellationToken);

        // Assert
        actual.Should().BeEquivalentTo(payload);
    }
}
