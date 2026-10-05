using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture]
public class BenchmarkTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public void Run_Eryri_FileDistributedCache_Tests() => BenchmarkRunner.Run<Eryri_FileDistributedCache_Tests>();
    [Test]
    public void Run_DamianH_FileDistributedCache_Tests() => BenchmarkRunner.Run<DamianH_FileDistributedCache_Tests>();

    public class Eryri_FileDistributedCache_Tests : TestBase
    {
        public Eryri_FileDistributedCache_Tests()
        {
            services.AddDistributedFileCache(d =>
            {
                d.SizeLimitBytes = SizeLimit;
                d.CacheDirectory = cacheDirectory.FullName;
            });
        }
    }

    public class DamianH_FileDistributedCache_Tests : TestBase
    {
        public DamianH_FileDistributedCache_Tests()
        {
            FileDistributedCacheServiceCollectionExtensions.AddFileDistributedCache(services, d =>
            {
                d.CacheDirectory = cacheDirectory.FullName;
                d.MaxTotalSize = SizeLimit;
            });
        }
    }

    [MarkdownExporter]
    [SimpleJob(
        warmupCount: 1,
        iterationCount: 5,
        invocationCount: 30)]
    public abstract class TestBase : IDisposable
    {
        [Params(100)]
        public int ParallelOperations { get; set; }
        public const int NumberOfItemsUntilFull = SizeLimit / PayloadSize;
        public const int PayloadSize = 4 << 10; // 4 KiB
        public const int SizeLimit = 1 << 20; // 1 MiB
        protected readonly DirectoryInfo cacheDirectory = Directory.CreateTempSubdirectory();
        private string key = Guid.NewGuid().ToString("N");
        private readonly byte[] payload = RandomNumberGenerator.GetBytes(PayloadSize);
        private readonly DistributedCacheEntryOptions cacheEntryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(2),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };
        protected readonly IServiceCollection services = new ServiceCollection();
        private IDisposable? disposable;
        private IDistributedCache? cache;
        private IBufferDistributedCache? bufferedCache;

        [GlobalSetup]
        public void Setup()
        {
            var sp = services.BuildServiceProvider();
            disposable = sp;
            cache = sp.GetRequiredService<IDistributedCache>();
            bufferedCache = sp.GetService<IBufferDistributedCache>();

            Parallel.For(0, NumberOfItemsUntilFull, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                cache.Set(key, payload, cacheEntryOptions);
            });
        }

        [GlobalCleanup]
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                disposable?.Dispose();
                Directory.Delete(cacheDirectory.FullName, recursive: true);
            }
        }

        [Benchmark]
        public void Set()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                cache!.Set(key, payload, cacheEntryOptions);
            });
        }

        [Benchmark]
        public void SetBuffered()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                bufferedCache!.Set(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions);
            });
        }

        [Benchmark]
        public async Task SetAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                var key = Guid.NewGuid().ToString("N");
                await cache!.SetAsync(key, payload, cacheEntryOptions, ct);
            });
        }

        [Benchmark]
        public async Task SetBufferedAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                var key = Guid.NewGuid().ToString("N");
                await bufferedCache!.SetAsync(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions, ct);
            });
        }

        [Benchmark]
        public void Get()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                if (cache!.Get(key) == null)
                {
                    cache!.Set(key, payload, cacheEntryOptions);
                }
            });
        }

        [Benchmark]
        public void GetBuffered()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                var buffer = new ArrayBufferWriter<byte>(payload.Length);
                if (bufferedCache!.TryGet(key, buffer))
                {
                    bufferedCache!.Set(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions);
                }
            });
        }

        [Benchmark]
        public async Task GetAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                if (await cache!.GetAsync(key, ct) == null)
                {
                    await cache!.SetAsync(key, payload, cacheEntryOptions, ct);
                }
            });
        }

        [Benchmark]
        public async Task GetBufferedAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                var buffer = new ArrayBufferWriter<byte>(payload.Length);
                if (!await bufferedCache!.TryGetAsync(key, buffer, ct))
                {
                    await bufferedCache!.SetAsync(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions, ct);
                }
            });
        }
    }
}
