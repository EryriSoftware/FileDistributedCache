# [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)

[![NuGet](https://img.shields.io/nuget/v/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)

**Keep more reusable data without giving more RAM to your cache—or running another cache service.**

`Eryri.FileDistributedCache` stores cache values on the local filesystem behind .NET's `IDistributedCache` and `IBufferDistributedCache` APIs.

Give it a byte limit and it evicts entries to make room *before* admitting a new write; expired entries are cleaned up automatically.

Use an **ephemeral cache** when you only need local disk capacity, or provide a persistent cache directory when you want entries to **survive process restarts**. Persistent caches use a write-ahead log with periodic snapshots to recover efficiently without keeping the entire cache index in memory.

It is built for a **single process**, with concurrent access, configurable eviction, synchronous and asynchronous operations, and Native AOT compatibility.

Use it directly when local disk is the right cache, or register it as the secondary provider for `HybridCache` when you want a memory-first cache with a larger local-disk tier.

> [!IMPORTANT]
> **Local, single-process.** `IDistributedCache` is the interface this package implements, not a promise of distributed storage. Each process should have its own cache; do not use this provider when replicas must share entries.

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
| Method           | Mean      | Error     | StdDev    | Median    | Gen0   | Gen1   | Allocated |
|----------------- |----------:|----------:|----------:|----------:|-------:|-------:|----------:|
| Set              | 0.1399 ms | 0.2792 ms | 0.0432 ms | 0.1199 ms |      - |      - |    1.2 KB |
| SetBuffered      | 0.1834 ms | 0.6407 ms | 0.0992 ms | 0.1356 ms |      - |      - |   1.36 KB |
| SetAsync         | 0.1779 ms | 0.5594 ms | 0.0866 ms | 0.1426 ms |      - |      - |   1.93 KB |
| SetBufferedAsync | 0.1795 ms | 0.5731 ms | 0.0887 ms | 0.1407 ms | 0.3333 |      - |   2.27 KB |
| Get              | 0.0610 ms | 0.0257 ms | 0.0067 ms | 0.0655 ms | 1.6667 |      - |   4.58 KB |
| GetBuffered      | 0.0662 ms | 0.0075 ms | 0.0019 ms | 0.0672 ms | 3.6667 | 0.6667 |  12.79 KB |
| GetAsync         | 0.0736 ms | 0.0111 ms | 0.0029 ms | 0.0742 ms | 1.6667 |      - |   5.32 KB |
| GetBufferedAsync | 0.0698 ms | 0.0121 ms | 0.0031 ms | 0.0678 ms | 4.0000 | 0.6667 |  13.68 KB |

### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)
| Method           | Mean      | Error     | StdDev    | Median    | Gen0   | Gen1   | Allocated |
|----------------- |----------:|----------:|----------:|----------:|-------:|-------:|----------:|
| Set              | 1.5264 ms | 4.3231 ms | 1.1227 ms | 1.0749 ms | 2.0000 | 0.3333 |   7.56 KB |
| SetBuffered      | 1.0781 ms | 1.9726 ms | 0.5123 ms | 0.9659 ms | 1.6667 | 0.3333 |   7.56 KB |
| SetAsync         | 2.1514 ms | 6.6831 ms | 1.7356 ms | 1.5289 ms | 1.3333 |      - |   7.31 KB |
| SetBufferedAsync | 1.1642 ms | 2.5317 ms | 0.6575 ms | 1.2555 ms | 1.3333 | 0.3333 |   7.31 KB |
| Get              | 0.1244 ms | 0.0229 ms | 0.0035 ms | 0.1242 ms | 4.6667 | 0.3333 |  13.28 KB |
| GetBuffered      | 0.1472 ms | 0.0471 ms | 0.0122 ms | 0.1441 ms | 4.3333 | 1.3333 |  13.32 KB |
| GetAsync         | 0.1820 ms | 0.0325 ms | 0.0084 ms | 0.1776 ms | 4.6667 | 1.0000 |  15.12 KB |
| GetBufferedAsync | 0.1738 ms | 0.0126 ms | 0.0019 ms | 0.1737 ms | 4.6667 | 1.0000 |  15.19 KB |

> [!Warning]
> - Size limits are eventual, not strict.
> - Write methods publishes without checking MaxTotalSize or MaxEntries
> - Eviction only acts on its periodic scan. LRU policy only.

### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)
| Method           | Mean     | Error     | StdDev    | Gen0   | Allocated |
|----------------- |---------:|----------:|----------:|-------:|----------:|
| Set              | 4.201 ms | 5.1480 ms | 1.3369 ms | 0.3333 |   1.59 KB |
| SetAsync         | 3.563 ms | 4.8485 ms | 1.2591 ms | 0.3333 |   1.37 KB |
| Get              | 1.658 ms | 0.8554 ms | 0.1324 ms | 1.3333 |    5.5 KB |
| GetAsync         | 1.629 ms | 0.6102 ms | 0.1585 ms | 1.3333 |   5.77 KB |

> [!Warning]
> - Doesn't implement `IBufferDistributedCache`.
> - Doesn't enforce a disk usage limit or maximum cache size.

### [LiteDb.Extensions.Caching](https://www.nuget.org/packages/LiteDb.Extensions.Caching)
| Method           | Mean      | Error      | StdDev    | Median    | Gen0    | Gen1   | Allocated |
|----------------- |----------:|-----------:|----------:|----------:|--------:|-------:|----------:|
| Set              | 3.9888 ms | 11.0469 ms | 2.8689 ms | 2.0319 ms | 16.3333 | 4.0000 |  70.72 KB |
| SetAsync         | 1.8872 ms |  1.6183 ms | 0.2504 ms | 1.9066 ms | 16.3333 | 2.3333 |  69.62 KB |
| Get              | 0.0304 ms |  0.0045 ms | 0.0007 ms | 0.0303 ms |  7.3333 | 0.6667 |  30.28 KB |
| GetAsync         | 0.0216 ms |  0.0180 ms | 0.0047 ms | 0.0183 ms |  5.0000 |      - |  20.61 KB |

> [!Warning]
> - Doesn't implement `IBufferDistributedCache`.
> - Doesn't enforce a disk usage limit or maximum cache size.

### [Caching.FileBackedDistributedCache](https://www.nuget.org/packages/Caching.FileBackedDistributedCache)
| Method           | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|----------------- |----------:|----------:|----------:|-------:|-------:|----------:|
| Set              | 0.2634 ms | 0.1248 ms | 0.0193 ms | 2.0000 | 0.3333 |   8.39 KB |
| SetAsync         | 0.2766 ms | 0.2580 ms | 0.0399 ms | 2.6667 | 0.6667 |   9.46 KB |
| Get              | 0.0716 ms | 0.0092 ms | 0.0024 ms | 3.6667 | 0.3333 |  11.86 KB |
| GetAsync         | 0.0818 ms | 0.0101 ms | 0.0026 ms | 3.6667 | 0.3333 |  13.08 KB |

> [!Warning]
> - Doesn't implement `IBufferDistributedCache`.
> - Doesn't enforce a disk usage limit or maximum cache size.
> - Expired entries are treated as cache misses but their files are not automatically deleted, so disk usage can grow indefinitely.
> - Doesn't implement automatic background eviction or cleanup of expired entries.

### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

> [!Warning]
> - Retains the entire cache in-memory. The filesystem is used as persistance/distribution mechanism.
> - Doesn't support SlidingExpiration.
> - Doesn't implement `IBufferDistributedCache`.