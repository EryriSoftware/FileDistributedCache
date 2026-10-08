# [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)

[![NuGet](https://img.shields.io/nuget/v/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)

**Keep more reusable data without giving more RAM to your cache—or running another cache service.** Eryri.FileDistributedCache stores cache values on the local filesystem behind .NET's `IDistributedCache` and `IBufferDistributedCache` APIs.

Give it a byte limit and it evicts entries to make room *before* admitting a new write; expired entries are cleaned up automatically.

It is built for a **single process**, with concurrent access, configurable eviction, synchronous and asynchronous operations, and Native AOT compatibility.

Use it directly when local disk is the right cache, or register it as the secondary provider for `HybridCache` when you want a memory-first cache with a larger local-disk tier.

> [!IMPORTANT]
> **Local, single-process.** `IDistributedCache` is the interface this package implements, not a promise of distributed storage. Each process should have its own cache; Do not use this provider when replicas must share entries.

## Why use it?

- **Keep RAM for your application.** Store reusable payloads in files rather than retaining the whole disk-backed cache in memory. A local filesystem is useful when your reusable working set is larger than the memory budget you want to assign to caching.
- **Keep writes moving near capacity.** Set a byte limit and the cache evicts entries when needed to make room *before* adding a new one. Choose LRU, LFU, FIFO or TTL to decide what goes first.
- **Let expired data clean itself up.** Absolute and sliding expiration are supported. A background cleanup timer is rescheduled for the next expiry, using a priority queue rather than relying solely on fixed-interval sweeps.
- **Fit familiar .NET APIs.** Use `IDistributedCache` or `IBufferDistributedCache`; the implementation is thread-safe and Native AOT-compatible.
- **Avoid a new service for a local need.** There is no cache server to deploy just to give one process more disposable cache capacity. The trade-off is that entries are not shared across processes.

**Good fit:** a single-process service or worker with reproducible cache values and useful local disk space. **Not a fit:** cross-replica consistency. The cache's configured byte limit controls its own entries; it does not reserve free space for other applications.

## Scope at a glance

| Requirement | This package |
| --- | --- |
| Familiar .NET caching interface | `IDistributedCache` and `IBufferDistributedCache` |
| Local disk for disposable cache values | Yes |
| Expiry eviction | Yes; automatic; rescheduled for the next expiry, using a priority queue rather than relying solely on fixed-interval sweeps |
| Capacity-based eviction | Yes; set a byte limit for automatic capacity eviction |
| Shared entries across application processes | No |
| Cache persistence across restarts | Yes; If a persistent Cache Directory is supplied |
| Automatic RAM-pressure-triggered spill | No; use as a disk-backed tier, not as an automatic memory overflow mechanism |

## Install and get started

The NuGet package ID is `Eryri.FileDistributedCache`.
```bash
dotnet add package Eryri.FileDistributedCache
```

The namespace is `Eryri.Extensions.Caching.FileSystem`.

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .BuildServiceProvider();

var cache = services.GetRequiredService<IDistributedCache>();

await cache.SetStringAsync(
    "product:42",
    "Cached product data",
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
    });

var value = await cache.GetStringAsync("product:42");
Console.WriteLine(value ?? "Cache miss");
```

Treat a miss as normal: an entry may have expired, been evicted, or disappeared when the process restarted. Retrieve or recompute the value from its source of truth and write it back.

### Use with [HybridCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid)

`HybridCache` can use an `IDistributedCache` provider as its secondary tier. Register this provider alongside `HybridCache` to put a local filesystem behind the usual in-memory tier. This is **not** a guarantee that values spill to disk only after RAM fills: `HybridCache` manages its tiers according to its own behavior and configuration.

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .AddHybridCache().Services
    .BuildServiceProvider();

var cache = services.GetRequiredService<HybridCache>();
var options = new HybridCacheEntryOptions
{
    Expiration = TimeSpan.FromMinutes(10),
    LocalCacheExpiration = TimeSpan.FromSeconds(5), // Keep in-memory items for only a short period
    Flags = HybridCacheEntryFlags.DisableLocalCache // Disables the 'IMemoryCache' for the purpose of thie example
};

await cache.SetAsync("product:42", "Cached product data", options);

var value = await cache.GetOrCreateAsync(
    "product:42",
    _ => ValueTask.FromResult("Value to create on a miss"),
    options);

Console.WriteLine(value);
```

Both tiers remain local to this process. For a shared secondary cache across replicas, use a backend designed for sharing.

## Expiration and capacity

Set a byte-based size limit if you want the cache to control its footprint. When a new entry would exceed that limit, the cache evicts existing entries as needed *before* adding it. The configured eviction policy determines which eligible entries are selected. Expired entries are also removed automatically by a background timer scheduled for the next expiry.

| Policy | Entry selected for capacity eviction | Useful when |
| --- | --- | --- |
| LRU | Least recently used | Recently accessed data is most likely to be needed again. |
| LFU | Least frequently used | Frequently requested data should survive short bursts of one-off reads. |
| FIFO | First added | Simple insertion order is preferable when older data becomes less useful over time. |
| TTL | Soonest to expire | Entries closest to expiry have the least remaining useful life. |

The package supports `DistributedCacheEntryOptions`:
- `AbsoluteExpiration`
- `AbsoluteExpirationRelativeToNow`
- `SlidingExpiration`.

Expiration controls whether an entry is valid; a capacity policy chooses which entries to remove to admit new data. The two are different mechanisms.

For manual capacity reduction, `IFileDistributedCache.Compact(decimal percentage)` selects entries according to the configured eviction policy. For example, `Compact(0.10)` requests removal of at least 10% of existing entries. This is useful even when no size limit is configured.

### Configuration reference

| Option | Purpose |
| --- | --- |
| `EvictionPolicy` | Selection policy for capacity eviction and compaction. |
| `SizeLimitBytes` | Maximum configured cache size in bytes. |
| `SizeLimitMiB` / `SizeLimitGiB` | Alternative units for configuring the byte limit. |
| `ExpirationScanFrequency` | Minimum interval between expiry cleanup runs; zero or a negative value schedules the next run for the soonest upcoming expiration. |
| `DefaultSlidingExpiration` | Default sliding expiry where an entry does not specify one. |
| `DefaultAbsoluteExpirationRelativeToNow` | Default relative absolute expiry, calculated when an entry is added. |
| `CacheDirectory` | The persistent directory for cache files. No value indicates an ephemeral tmp directory should be used which will be deleted during normal shutdown. |
| `SnapshotInterval` | If a persistent directory is supplied, determines how often the Write Ahead Log is compacted into a snapshot. |

## Operational boundaries

- **One process owns one cache.** Do not share the cache directory or assume another process can observe its entries safely.
- **Misses are expected.** Cached values must be safe to lose and regenerable after expiration, eviction, or storage failure.
- **Filesystem access matters.** The process needs permission to create, read, write, and delete files in its cache directory. A configured cache-size limit does not protect against another workload filling the underlying filesystem.
- **Write failures:** If the filesystem rejects a write, the entry is not added and an error is logged. Callers should not treat `Set` as proof that the entry can later be read back.

## Benchmarks

The benchmark that matters most for this package is **sustained operation with realistic workloads while the cache is already full**: this tests the cost of making room, not just writing into an empty directory. The figures below are useful directional evidence, not a cross-machine performance guarantee.

- PayloadSize=4KiB
- CacheSizeLimit=1MiB
- ParallelOperations=100
- InvocationCount=30
- IterationCount=5
- UnrollFactor=1  
- WarmupCount=1

Tests located here: [BenchmarkTests.cs](https://github.com/EryriSoftware/FileDistributedCache/blob/main/src/Extensions.Caching.FileSystem.Tests/Performance/BenchmarkTests.cs)
> 1s == 1000ms, 1ms == 1000us, 1us == 1000ns

### [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)
| Method           | Mean         | StdDev       | Median       |
|----------------- |-------------:|-------------:|-------------:|
| Set              | 12,879.29 us |   955.965 us | 12,722.89 us |
| SetBuffered      | 17,960.79 us | 8,211.567 us | 13,137.58 us |
| SetAsync         | 14,512.19 us | 2,845.222 us | 13,345.48 us |
| SetBufferedAsync | 13,184.98 us |   399.919 us | 13,134.07 us |
| Get              |  6,154.76 us |   216.441 us |  6,256.37 us |
| GetBuffered      |     85.20 us |     4.742 us |     83.76 us |
| GetAsync         |  6,782.77 us |    81.746 us |  6,786.04 us |
| GetBufferedAsync |  7,541.43 us |   359.949 us |  7,579.14 us |

### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)
| Method           | Mean       | StdDev      | Median     |
|----------------- |-----------:|------------:|-----------:|
| Set              | 155.053 ms | 114.0786 ms | 110.185 ms |
| SetBuffered      | 201.617 ms | 131.7409 ms | 201.687 ms |
| SetAsync         | 129.286 ms |  71.6261 ms | 115.980 ms |
| SetBufferedAsync | 116.472 ms |  49.0898 ms | 117.823 ms |
| Get              |  14.384 ms |   0.5454 ms |  14.570 ms |
| GetBuffered      |   1.013 ms |   0.0577 ms |   1.003 ms |
| GetAsync         |  18.109 ms |   0.1486 ms |  18.063 ms |
| GetBufferedAsync |  18.546 ms |   0.4131 ms |  18.373 ms |

> [!Warning]
> - Size limits are eventual, not strict.
> - Write methods publishes without checking MaxTotalSize or MaxEntries
> - Eviction only acts on its periodic scan.

### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

> [!Warning]
> - Retains the entire cache in-memory. The filesystem is used as persistance/distribution mechanism.
> - Doesn't support SlidingExpiration.
> - Doesn't implement `IBufferDistributedCache`.