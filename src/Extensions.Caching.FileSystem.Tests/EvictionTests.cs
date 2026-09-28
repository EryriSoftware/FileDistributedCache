using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class EvictionTests
{
    private byte[] Bytes(int length = 100) => RandomNumberGenerator.GetBytes(length);

    [Test]
    public void Expired_Entries_Are_Removed()
    {
        // Arrange
        using var ctx = new CacheContext();

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(11) };

        ctx.Cache.Set("1", Bytes(100), options1);
        ctx.Cache.Set("2", Bytes(100), options2);
        ctx.Cache.Size.Should().Be(200);

        // Act
        ctx.Advance(TimeSpan.FromDays(2));

        // Assert
        ctx.Cache.Size.Should().Be(100);
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
        ctx.Cache.Compact(0.5m);

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
        ctx.Cache.Get("1").Should().NotBeNull();
        ctx.Cache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().NotBeNull(because: "entry should remain");
        ctx.Cache.Get("2").Should().BeNull(because: "LRU entry should be removed by Compact");
    }

    [Test]
    public void Compact_Removes_Least_Frequently_Used_Entries()
    {
        // Arrange
        using var ctx = new CacheContext(EvictionPolicy.LFU);

        var options1 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };
        var options2 = new DistributedCacheEntryOptions { AbsoluteExpiration = ctx.Now.AddDays(1) };

        ctx.Cache.Set("1", Bytes(), options1);
        ctx.Cache.Set("2", Bytes(), options2);

        // Act
        ctx.Cache.Get("2").Should().NotBeNull();
        ctx.Cache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().BeNull(because: "LFU entry should be removed by Compact");
        ctx.Cache.Get("2").Should().NotBeNull(because: "entry should remain");
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
        ctx.Cache.Compact(0.5m);

        // Assert
        ctx.Cache.Get("1").Should().BeNull(because: "FIFO entry should be removed by Compact");
        ctx.Cache.Get("2").Should().NotBeNull(because: "entry should remain");
    }
}
