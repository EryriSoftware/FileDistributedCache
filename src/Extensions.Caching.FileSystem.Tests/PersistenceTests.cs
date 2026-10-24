using System.Security.Cryptography;
using Eryri.Extensions.Caching.FileSystem.Tests.Contexts;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
internal class PersistenceTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;
    private static IEnumerable<TestCaseData> TestCases()
    {
        yield return new TestCaseData(true).SetName("With Snapshot");
        yield return new TestCaseData(false).SetName("With WAL");
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task Is_Persistant(bool useSnapshot)
    {
        // Arrange
        var keys = Enumerable.Range(0, 1000).Select(d => Guid.NewGuid().ToString("N")).ToArray();
        var directory = Directory.CreateTempSubdirectory();
        var payload = RandomNumberGenerator.GetBytes(10);

        try
        {
            using (var ctx = new CacheContext(cacheDirectory: directory.FullName))
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = ctx.Now.AddDays(1000),
                    SlidingExpiration = TimeSpan.FromDays(1000)
                };

                await Parallel.ForEachAsync(keys, CancellationToken, async (key, ct) =>
                {
                    // Act
                    await ctx.Cache.SetAsync(key, payload, options, ct);
                    var actual = await ctx.Cache.GetAsync(key, ct);

                    // Assert
                    actual.Should().BeEquivalentTo(payload);
                });

                if (useSnapshot)
                {
                    ctx.Advance(TimeSpan.FromDays(1));
                    await Task.Delay(100);
                }
            }

            using (var ctx = new CacheContext(cacheDirectory: directory.FullName))
            {
                await Parallel.ForEachAsync(keys, CancellationToken, async (key, ct) =>
                {
                    // Act
                    var actual = await ctx.Cache.GetAsync(key, ct);

                    // Assert
                    actual.Should().BeEquivalentTo(payload);
                });
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
