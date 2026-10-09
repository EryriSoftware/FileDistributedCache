using System.Security.Cryptography;
using Eryri.Extensions.Caching.FileSystem.Tests.Contexts;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class EvictionTests
{
    private byte[] Bytes(int length = 100) => RandomNumberGenerator.GetBytes(length);

    [Test]
    public void Expired_Entries_With_AbsoluteExpiration_Are_Removed()
    {
        // Arrange
        using var ctx = new CacheContext();

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(11) };

        ctx.Cache.Set("1", Bytes(100), options1);
        ctx.Cache.Set("2", Bytes(100), options2);
        ctx.FileCache.Size.Should().Be(200);

        // Act
        ctx.Advance(TimeSpan.FromDays(2));

        // Assert
        ctx.FileCache.Size.Should().Be(100);
        ctx.Cache.Get("1").Should().BeNull();
        ctx.Cache.Get("2").Should().NotBeNull();
    }

    [Test]
    public async Task Expired_Entries_With_SlidingExpiration_Are_Removed()
    {
        // Arrange
        using var ctx = new CacheContext();

        var options1 = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(1) };
        var options2 = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(11) };

        ctx.Cache.Set("1", Bytes(100), options1);
        ctx.Cache.Set("2", Bytes(100), options2);
        ctx.FileCache.Size.Should().Be(200);

        // Act
        ctx.Advance(TimeSpan.FromDays(10));

        // Assert
        ctx.FileCache.Size.Should().Be(100);
        ctx.Cache.Get("1").Should().BeNull();
        ctx.Cache.Get("2").Should().NotBeNull();

        // Act
        ctx.Advance(TimeSpan.FromDays(10));

        // Assert
        ctx.FileCache.Size.Should().Be(100);

        // Act
        ctx.Advance(TimeSpan.FromDays(100));

        // Assert
        ctx.FileCache.Size.Should().Be(0);
        ctx.Cache.Get("2").Should().BeNull();
    }

    [Test]
    public async Task Expired_Entries_With_AbsoluteExpiration_Takes_Precidence_Over_SlidingExpiration()
    {
        // Arrange
        using var ctx = new CacheContext();

        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromDays(2),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(10)
        };

        ctx.Cache.Set("1", Bytes(100), options);
        ctx.FileCache.Size.Should().Be(100);

        for (int i = 0; i < 9; i++)
        {
            // Act
            ctx.Advance(TimeSpan.FromDays(1));

            // Assert
            ctx.FileCache.Size.Should().Be(100);
            ctx.Cache.Get("1").Should().NotBeNull();
        }

        // Act
        ctx.Advance(TimeSpan.FromDays(1));

        // Assert
        ctx.FileCache.Size.Should().Be(0);
        ctx.Cache.Get("1").Should().BeNull();
    }

    [Test]
    public void Compact_Removes_Soonest_TTL_Entries()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.TTL);

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(10) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };

        ctx.Cache.Set("1", Bytes(), options1);
        ctx.Cache.Set("2", Bytes(), options2);

        // Act
        ctx.FileCache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().NotBeNull(because: "entry should remain");
        ctx.Cache.Get("2").Should().BeNull(because: "next expiring entry should be removed by Compact");
    }

    [Test]
    public void Compact_Removes_Least_Recently_Used_Entries()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LRU);

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };

        ctx.Cache.Set("1", Bytes(), options1);
        ctx.Cache.Set("2", Bytes(), options2);

        // Act
        ctx.Cache.Get("2").Should().NotBeNull();
        ctx.Cache.Refresh("1"); // Increments Version and LastAccessTicks
        ctx.FileCache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().NotBeNull(because: "entry should remain");
        ctx.Cache.Get("2").Should().BeNull(because: "LRU entry should be removed by Compact");
    }

    [Test]
    public void Compact_Removes_Least_Frequently_Used_Entries()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LFU);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ctx.Now.AddDays(1)
        };

        ctx.Cache.Set("1", Bytes(), options);
        ctx.Cache.Set("2", Bytes(), options);

        // Act
        ctx.Cache.Get("2").Should().NotBeNull();
        ctx.Cache.Refresh("1"); // Increments Version without changing AccessCount

        ctx.FileCache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().BeNull(
            because: "the least frequently used entry should be removed");

        ctx.Cache.Get("2").Should().NotBeNull(
            because: "the more frequently used entry should remain");
    }

    [Test]
    public void Compact_Removes_First_In_First_Out_Entries()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.FIFO);

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };

        ctx.Cache.Set("1", Bytes(), options1);
        ctx.Cache.Set("2", Bytes(), options2);

        // Act
        ctx.Cache.Set("1", Bytes(), options1);
        ctx.FileCache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().BeNull(because: "FIFO entry should be removed by Compact");
        ctx.Cache.Get("2").Should().NotBeNull(because: "entry should remain");
    }
}
