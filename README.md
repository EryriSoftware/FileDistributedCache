# [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)

**Keep more reusable data without giving more RAM to your cache—or running another cache service.** Eryri.FileDistributedCache stores disposable cache values on the local filesystem behind .NET's `IDistributedCache` and `IBufferDistributedCache` APIs. Give it a byte limit and it evicts entries to make room *before* admitting a new write; expired entries are cleaned up automatically.

It is built for a **single process**, with concurrent access, configurable eviction, synchronous and asynchronous operations, and Native AOT compatibility. Use it directly when local disk is the right cache, or register it as the secondary provider for `HybridCache` when you want a memory-first cache with a larger local-disk tier.

> [!IMPORTANT]
> **Local, ephemeral, not shared.** `IDistributedCache` is the interface this package implements, not a promise of distributed storage. Each process has its own cache; entries are disposable and must be recoverable from your source of truth. Do not use this provider when replicas must share entries or cache contents must survive restarts.

## Why use it?

- **Keep RAM for your application.** Store reusable payloads in files rather than retaining the whole disk-backed cache in memory. A local filesystem is useful when your reusable working set is larger than the memory budget you want to assign to caching.
- **Keep writes moving near capacity.** Set a byte limit and the cache evicts entries when needed to make room *before* adding a new one. Choose LRU, LFU, FIFO or TTL to decide what goes first.
- **Let expired data clean itself up.** Absolute and sliding expiration are supported. A background cleanup timer is rescheduled for the next expiry, using a priority queue rather than relying solely on fixed-interval sweeps.
- **Fit familiar .NET APIs.** Use `IDistributedCache` or `IBufferDistributedCache`; the implementation is thread-safe and Native AOT-compatible.
- **Avoid a new service for a local need.** There is no cache server to deploy just to give one process more disposable cache capacity. The trade-off is that entries are not shared across processes.

**Good fit:** a single-process service or worker with reproducible cache values and useful local disk space. **Not a fit:** cross-replica consistency, persistence, or a filesystem you cannot afford to fill with disposable data. The cache's configured byte limit controls its own entries; it does not reserve free space for other applications.

## Scope at a glance

| Requirement | This package |
| --- | --- |
| Familiar .NET caching interface | `IDistributedCache` and `IBufferDistributedCache` |
| Local disk for disposable cache values | Yes |
| Expiry eviction | Yes; automatic; rescheduled for the next expiry, using a priority queue rather than relying solely on fixed-interval sweeps |
| Capacity-based eviction | Yes; set a byte limit for automatic capacity eviction |
| Shared entries across application processes | No |
| Cache persistence across restarts | No |
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

The package supports `DistributedCacheEntryOptions`: `AbsoluteExpiration`, `AbsoluteExpirationRelativeToNow`, and `SlidingExpiration`. Expiration controls whether an entry is valid; a capacity policy chooses which entries to remove to admit new data. The two are different mechanisms.

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

## Operational boundaries

- **One process owns one cache.** Do not share the cache directory or assume another process can observe its entries safely.
- **Storage is temporary.** The cache creates a temporary folder under the current user's temp directory at startup and removes it during normal shutdown. Crashes or forced termination may leave files behind; do not rely on shutdown cleanup for durability or guaranteed reclamation.
- **Misses are expected.** Cached values must be safe to lose and regenerable after expiration, eviction, restart, or storage failure.
- **Filesystem access matters.** The process needs permission to create, read, write, and delete files in its temp directory. A configured cache-size limit does not protect against another workload filling the underlying filesystem.
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
| Method           | Mean      | Error     | StdDev    | Median    |
|----------------- |----------:|----------:|----------:|----------:|
| Set              | 31.763 ms | 25.644 ms |  3.968 ms | 33.555 ms |
| SetBuffered      | 44.576 ms | 74.168 ms | 19.261 ms | 35.198 ms |
| SetAsync         | 33.106 ms | 35.982 ms |  5.568 ms | 35.104 ms |
| SetBufferedAsync | 30.608 ms | 15.294 ms |  3.971 ms | 30.953 ms |
| Get              |  6.046 ms |  1.094 ms |  0.284 ms |  6.110 ms |
| GetBuffered      |  0.067 ms |  0.022 ms |  0.003 ms |  0.067 ms |
| GetAsync         |  6.421 ms |  0.359 ms |  0.055 ms |  6.435 ms |
| GetBufferedAsync |  7.362 ms |  2.345 ms |  0.609 ms |  7.061 ms |

> [!Note]
> This cache is ephemeral, designed for single-process use.

### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)
| Method           | Mean      | Error       | StdDev     | Median    |
|----------------- |----------:|------------:|-----------:|----------:|
| Set              | 68.738 ms | 142.9623 ms | 22.1236 ms | 65.681 ms |
| SetBuffered      | 69.703 ms |  90.9287 ms | 23.6139 ms | 65.810 ms |
| SetAsync         | 76.828 ms | 117.5669 ms | 30.5318 ms | 59.628 ms |
| SetBufferedAsync | 56.322 ms |  54.4735 ms |  8.4298 ms | 53.331 ms |
| Get              | 13.735 ms |   4.4539 ms |  0.6892 ms | 13.693 ms |
| GetBuffered      |  1.028 ms |   0.1026 ms |  0.0266 ms |  1.033 ms |
| GetAsync         | 17.698 ms |   1.2860 ms |  0.3340 ms | 17.499 ms |
| GetBufferedAsync | 17.886 ms |   2.2197 ms |  0.3435 ms | 17.830 ms |

> [!Note]
> This cache is designed for single-process use.

> [!Warning]
> Size limits are eventual, not strict. Write methods publishes without checking MaxTotalSize or MaxEntries; eviction only acts on its periodic scan.

### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

> [!Warning]
> - Retains the entire cache in-memory. The filesystem is used as persistance/distribution mechanism.
> - Doesn't support SlidingExpiration.
> - Doesn't implement `IBufferDistributedCache`.