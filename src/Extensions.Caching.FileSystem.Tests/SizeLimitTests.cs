using System.Security.Cryptography;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class SizeLimitTests
{
    private Fixture fixture = new Fixture();
    private CancellationToken CancellationToken = TestContext.CurrentContext.CancellationToken;
    private byte[] Bytes(int length = 100) => RandomNumberGenerator.GetBytes(length);

    [Test]
    public void LRU_Eviction_Removes_Least_Recently_Used()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LRU, sizeLimitBytes: 4);

        ctx.Cache.Set("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });
        ctx.Cache.Set("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Act
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Set("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Assert
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Get("b").Should().BeNull(because: "LRU should evict least recently used entry (b)");
        ctx.Cache.Get("c").Should().NotBeNull();
    }

    [Test]
    public async Task LRU_Async_Eviction_Removes_Least_Recently_Used()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LRU, sizeLimitBytes: 4);

        await ctx.Cache.SetAsync("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);
        await ctx.Cache.SetAsync("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Act
        await ctx.Cache.GetAsync("a", CancellationToken);
        await ctx.Cache.SetAsync("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Assert
        var actual = await ctx.Cache.GetAsync("a", CancellationToken);
        actual.Should().NotBeNull();
        actual = await ctx.Cache.GetAsync("b", CancellationToken);
        actual.Should().BeNull(because: "LRU should evict least recently used entry (b)");
        actual = await ctx.Cache.GetAsync("c", CancellationToken);
        actual.Should().NotBeNull();
    }

    [Test]
    public void LFU_Eviction_Removes_Least_Frequently_Used()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LFU, sizeLimitBytes: 4);

        ctx.Cache.Set("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });
        ctx.Cache.Set("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Act
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Set("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Assert
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Get("b").Should().BeNull(because: "LFU should evict least frequently used entry (b)");
        ctx.Cache.Get("c").Should().NotBeNull();
    }

    [Test]
    public async Task LFU_Async_Eviction_Removes_Least_Frequently_Used()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LFU, sizeLimitBytes: 4);

        await ctx.Cache.SetAsync("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);
        await ctx.Cache.SetAsync("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Act
        await ctx.Cache.GetAsync("a", CancellationToken);
        await ctx.Cache.SetAsync("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Assert
        var actual = await ctx.Cache.GetAsync("a", CancellationToken);
        actual.Should().NotBeNull();
        actual = await ctx.Cache.GetAsync("b", CancellationToken);
        actual.Should().BeNull(because: "LFU should evict least frequently used entry (b)");
        actual = await ctx.Cache.GetAsync("c", CancellationToken);
        actual.Should().NotBeNull();
    }

    [Test]
    public void TTL_Eviction_Removes_Soonest_Expiring()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.TTL, sizeLimitBytes: 4);

        ctx.Cache.Set("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });
        ctx.Cache.Set("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddMinutes(1) });

        // Act
        ctx.Cache.Set("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Assert
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Get("b").Should().BeNull(because: "TTL should evict soonest expiring entry (b)");
        ctx.Cache.Get("c").Should().NotBeNull();
    }

    [Test]
    public async Task TTL_Async_Eviction_Removes_Soonest_Expiring()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.TTL, sizeLimitBytes: 4);

        await ctx.Cache.SetAsync("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);
        await ctx.Cache.SetAsync("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddMinutes(1) }, CancellationToken);

        // Act
        await ctx.Cache.SetAsync("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Assert
        var actual = await ctx.Cache.GetAsync("b", CancellationToken);
        actual.Should().BeNull(because: "TTL should evict soonest expiring entry (b)");
        actual = await ctx.Cache.GetAsync("a", CancellationToken);
        actual.Should().NotBeNull();
        actual = await ctx.Cache.GetAsync("c", CancellationToken);
        actual.Should().NotBeNull();
    }

    [Test]
    public void FIFO_Eviction_Removes_First()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.FIFO, sizeLimitBytes: 4);

        ctx.Cache.Set("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });
        ctx.Cache.Set("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Act
        ctx.Cache.Get("a").Should().NotBeNull();
        ctx.Cache.Set("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) });

        // Assert
        ctx.Cache.Get("a").Should().BeNull(because: "FIFO should evict first entry added (a)");
        ctx.Cache.Get("b").Should().NotBeNull();
        ctx.Cache.Get("c").Should().NotBeNull();
    }

    [Test]
    public async Task FIFO_Async_Eviction_Removes_First()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.FIFO, sizeLimitBytes: 4);

        await ctx.Cache.SetAsync("a", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);
        await ctx.Cache.SetAsync("b", Bytes(1), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Act
        var actual = await ctx.Cache.GetAsync("a", CancellationToken);
        actual.Should().NotBeNull();
        await ctx.Cache.SetAsync("c", Bytes(3), new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddHours(1) }, CancellationToken);

        // Assert
        actual = await ctx.Cache.GetAsync("a", CancellationToken);
        actual.Should().BeNull(because: "FIFO should evict first entry added (a)");
        actual = await ctx.Cache.GetAsync("b", CancellationToken);
        actual.Should().NotBeNull();
        actual = await ctx.Cache.GetAsync("c", CancellationToken);
        actual.Should().NotBeNull();
    }
}
