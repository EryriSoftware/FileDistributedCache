using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Eryri.Buffers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Perfolizer.Horology;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture]
public class BenchmarkTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;
    private readonly IConfig config = ManualConfig
        .Create(DefaultConfig.Instance)
        .WithSummaryStyle(SummaryStyle.Default.WithTimeUnit(TimeUnit.Microsecond));

    [TestCase(typeof(Eryri_FileDistributedCache_Benchmarks), TestName = "Eryri_FileDistributedCache")]
    [TestCase(typeof(DamianH_FileDistributedCache_Benchmarks), TestName = "DamianH_FileDistributedCache")]
    [TestCase(typeof(DNeoSmart_Caching_Sqlite_Benchmarks), TestName = "DNeoSmart_Caching_Sqlite")]
    [TestCase(typeof(LiteDb_Extensions_Caching_Benchmarks), TestName = "LiteDb_Extensions_Caching")]
    [TestCase(typeof(Caching_FileBackedDistributedCache_Benchmarks), TestName = "Caching_FileBackedDistributedCache")]
    [TestCase(typeof(Net_DistributedFileStoreCache_Benchmarks), TestName = "Net_DistributedFileStoreCache")]
    public void Run_Benchmark(Type benchmarkType) => BenchmarkRunner.Run(benchmarkType, config);

    public class Eryri_FileDistributedCache_Benchmarks : Benchmarks
    {
        [ParamsAllValues]
        public bool RunAtCapacity;

        public Eryri_FileDistributedCache_Benchmarks()
        {
            services.AddDistributedFileCache(d =>
            {
                d.SizeLimitBytes = RunAtCapacity ? InitialSize : null;
                d.CacheDirectory = cacheDirectory.FullName;
            });
        }
    }

    public class DamianH_FileDistributedCache_Benchmarks : Benchmarks
    {
        public DamianH_FileDistributedCache_Benchmarks()
        {
            FileDistributedCacheServiceCollectionExtensions.AddFileDistributedCache(services, d =>
            {
                d.CacheDirectory = cacheDirectory.FullName;
                d.MaxTotalSize = null;
            });
        }
    }

    public class DNeoSmart_Caching_Sqlite_Benchmarks : Benchmarks
    {
        public DNeoSmart_Caching_Sqlite_Benchmarks()
        {
            NeoSmart.Caching.Sqlite.AspSqliteCacheServiceCollectionExtensions.AddSqliteCache(services, d =>
            {
                d.CachePath = Path.Join(cacheDirectory.FullName, "cache.db");
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            base.Dispose(isDisposing);
        }
    }

    public class LiteDb_Extensions_Caching_Benchmarks : Benchmarks
    {
        public LiteDb_Extensions_Caching_Benchmarks()
        {
            LiteDb.Extensions.Caching.ServiceCollectionExtensions.AddLiteDbCache(services, d =>
            {
                d.CachePath = Path.Join(cacheDirectory.FullName, "cache.db");
            });
        }
    }

    public class Caching_FileBackedDistributedCache_Benchmarks : Benchmarks
    {
        public Caching_FileBackedDistributedCache_Benchmarks()
        {
            FileBackedCache.Extensions.AddFileBackedCache(services, cacheDirectory.FullName);
        }
    }

    public class Net_DistributedFileStoreCache_Benchmarks : Benchmarks
    {
        [ParamsAllValues]
        public bool RunAtCapacity;

        public Net_DistributedFileStoreCache_Benchmarks()
        {
            Net.DistributedFileStoreCache.RegisterDistributedFileStoreCache.AddDistributedFileStoreCache(services, d =>
            {
                d.PathToCacheFileDirectory = cacheDirectory.FullName;
                d.SecondPartOfCacheFileName = "cache";
                d.MaxBytesInJsonCacheFile = RunAtCapacity ? InitialSize : 10000;
                d.WhichVersion = Net.DistributedFileStoreCache.FileStoreCacheVersions.IDistributedCache;
            });
        }
    }

    [MemoryDiagnoser]
    [ShortRunJob]
    public abstract class Benchmarks : IDisposable
    {
        public const int ParallelOperations = 100;
        public const int InitialNumberOfItems = 10_000;
        public const int InitialSize = InitialNumberOfItems * PayloadSize;
        public const int PayloadSize = 4 << 10; // 4 KiB
        protected readonly DirectoryInfo cacheDirectory = Directory.CreateTempSubdirectory();
        private readonly string key = Guid.NewGuid().ToString("N");
        private readonly string missingKey = Guid.NewGuid().ToString("N");
        private readonly byte[] payload = RandomNumberGenerator.GetBytes(PayloadSize);
        protected readonly DistributedCacheEntryOptions cacheEntryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(2),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
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

            Parallel.For(0, InitialNumberOfItems, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                cache!.Set(key, payload, cacheEntryOptions);
            });
        }

        [IterationSetup]
        public void IterationSetup()
        {
            cache!.Set(key, payload, cacheEntryOptions);
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
                cacheDirectory.Delete(recursive: true);
            }
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void SetNewKey()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                cache!.Set(key, payload, cacheEntryOptions);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void SetNewKeyBuffered()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                var key = Guid.NewGuid().ToString("N");
                bufferedCache!.Set(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task SetNewKeyAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                var key = Guid.NewGuid().ToString("N");
                await cache!.SetAsync(key, payload, cacheEntryOptions, ct);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task SetNewKeyBufferedAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                var key = Guid.NewGuid().ToString("N");
                await bufferedCache!.SetAsync(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions, ct);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void SetExistingKey()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                cache!.Set(key, payload, cacheEntryOptions);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void SetExistingKeyBuffered()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                bufferedCache!.Set(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task SetExistingKeyAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                await cache!.SetAsync(key, payload, cacheEntryOptions, ct);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task SetExistingKeyBufferedAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                await bufferedCache!.SetAsync(key, new ReadOnlySequence<byte>(payload), cacheEntryOptions, ct);
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void Get()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                if (cache!.Get(key) == null)
                {
                    throw new MissingMemberException("Expected a cache hit.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void GetBuffered()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                using var buffer = new ArrayPoolBufferWriter<byte>(payload.Length);
                if (!bufferedCache!.TryGet(key, buffer))
                {
                    throw new MissingMemberException("Expected a cache hit.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task GetAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                if (await cache!.GetAsync(key, ct) == null)
                {
                    throw new MissingMemberException("Expected a cache hit.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task GetBufferedAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                using var buffer = new ArrayPoolBufferWriter<byte>(payload.Length);
                if (!await bufferedCache!.TryGetAsync(key, buffer, ct))
                {
                    throw new MissingMemberException("Expected a cache hit.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void GetMiss()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                if (cache!.Get(missingKey) != null)
                {
                    throw new InvalidOperationException("Expected a cache miss.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public void GetBufferedMiss()
        {
            Parallel.For(0, ParallelOperations, i =>
            {
                using var buffer = new ArrayPoolBufferWriter<byte>(payload.Length);
                if (bufferedCache!.TryGet(missingKey, buffer))
                {
                    throw new InvalidOperationException("Expected a cache miss.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task GetMissAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                if (await cache!.GetAsync(missingKey, ct) != null)
                {
                    throw new InvalidOperationException("Expected a cache miss.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task GetBufferedMissAsync()
        {
            await Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                using var buffer = new ArrayPoolBufferWriter<byte>(payload.Length);
                if (await bufferedCache!.TryGetAsync(missingKey, buffer, ct))
                {
                    throw new InvalidOperationException("Expected a cache miss.");
                }
            });
        }

        [Benchmark(OperationsPerInvoke = ParallelOperations)]
        public async Task SetGetExistingKeyAsync()
        {
            var setTasks = Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                await cache!.SetAsync(key, payload, cacheEntryOptions, ct);
            });

            var getTasks = Parallel.ForAsync(0, ParallelOperations, CancellationToken, async (i, ct) =>
            {
                if (await cache!.GetAsync(key, ct) == null)
                {
                    throw new MissingMemberException("Expected a cache hit.");
                }
            });

            await Task.WhenAll(setTasks, getTasks);
        }
    }
}
