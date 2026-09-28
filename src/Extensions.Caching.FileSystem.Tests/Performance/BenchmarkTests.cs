using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmark;

[TestFixture]
public class BenchmarkTests
{
    [Test]
    public void Run_Eryri_FileDistributedCache_Tests() => BenchmarkRunner.Run<Eryri_FileDistributedCache_Tests>();
    [Test]
    public void Run_Net_DistributedFileStoreCache_Tests() => BenchmarkRunner.Run<Net_DistributedFileStoreCache_Tests>();
    [Test]
    public void Run_DamianH_FileDistributedCache_Tests() => BenchmarkRunner.Run<DamianH_FileDistributedCache_Tests>();

    public class Eryri_FileDistributedCache_Tests : TestBase
    {
        public Eryri_FileDistributedCache_Tests()
        {
            services.AddDistributedFileCache(d => d.SizeLimitBytes = SizeLimit);
        }
    }

    public class Net_DistributedFileStoreCache_Tests : TestBase
    {
        public Net_DistributedFileStoreCache_Tests()
        {
            Net.DistributedFileStoreCache.RegisterDistributedFileStoreCache.AddDistributedFileStoreCache(services, d =>
            {
                d.PathToCacheFileDirectory = cacheDirectory.FullName;
                d.SecondPartOfCacheFileName = "cache";
                d.MaxBytesInJsonCacheFile = Convert.ToInt32(SizeLimit);
                d.WhichVersion = Net.DistributedFileStoreCache.FileStoreCacheVersions.IDistributedCache;
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
                d.MaxTotalSize = Convert.ToInt64(SizeLimit);
            });
        }
    }

    [MarkdownExporter]
    [SimpleJob(
        warmupCount: 1,
        iterationCount: 5,
        invocationCount: (int)SizeLimit * PayloadSize)]
    public abstract class TestBase : IDisposable
    {
        public const long SizeLimit = 300;
        public const int PayloadSize = 1;
        protected readonly DirectoryInfo cacheDirectory = Directory.CreateTempSubdirectory();
        private string key = Guid.NewGuid().ToString("N");
        private readonly byte[] payload = RandomNumberGenerator.GetBytes(PayloadSize);
        protected readonly IServiceCollection services = new ServiceCollection();
        private IDisposable? disposable;
        private IDistributedCache? cache;

        [GlobalSetup]
        public void Setup()
        {
            var sp = services.BuildServiceProvider();
            disposable = sp;
            cache = sp.GetRequiredService<IDistributedCache>();
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
        public void Write()
        {
            var key = Guid.NewGuid().ToString("N");
            cache!.Set(key, payload);
            this.key = key;
        }

        [Benchmark]
        public async Task WriteAsync()
        {
            var key = Guid.NewGuid().ToString("N");
            await cache!.SetAsync(key, payload);
            this.key = key;
        }

        [Benchmark]
        public void Read()
        {
            if (cache!.Get(key) == null)
            {
                Guid.NewGuid().ToString("N");
                cache!.Set(key, payload);
            }
        }

        [Benchmark]
        public async Task ReadAsync()
        {
            if (await cache!.GetAsync(key) == null)
            {
                Guid.NewGuid().ToString("N");
                await cache!.SetAsync(key, payload);
            }
        }
    }
}
