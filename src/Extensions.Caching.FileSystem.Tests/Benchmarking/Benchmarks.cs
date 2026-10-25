using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Eryri.Buffers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

[MemoryDiagnoser]
[MinColumn]
[MeanColumn]
[MedianColumn]
[SimpleJob]
public abstract class Benchmarks : IDisposable
{
    public const int ParallelOperations = 100;
    public const int InitialNumberOfItems = 10_000;
    public const int InitialSize = InitialNumberOfItems * PayloadSize;
    public const int PayloadSize = 4 << 10; // 4 KiB
    private CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;
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
