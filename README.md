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
| Method           | Mean      | Error      | StdDev    | Median    | Gen0   | Allocated |
|----------------- |----------:|-----------:|----------:|----------:|-------:|----------:|
| Set              | 138.74 us |  92.406 us | 14.300 us | 139.73 us |      - |    1139 B |
| SetBuffered      | 178.57 us | 538.275 us | 83.299 us | 150.10 us |      - |    1311 B |
| SetAsync         | 167.18 us | 499.066 us | 77.231 us | 130.37 us |      - |    1905 B |
| SetBufferedAsync | 147.58 us | 125.371 us | 19.401 us | 140.60 us | 0.3333 |    2261 B |
| Get              |  59.77 us |  17.320 us |  4.498 us |  59.67 us | 1.6667 |    4566 B |
| GetBuffered      |  65.29 us |   9.898 us |  2.570 us |  65.25 us |      - |     636 B |
| GetAsync         |  69.72 us |  12.847 us |  3.336 us |  69.56 us | 1.6667 |    5323 B |
| GetBufferedAsync |  70.98 us |  18.614 us |  2.881 us |  71.16 us | 0.3333 |    1555 B |

### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)
| Method           | Mean       | Error       | StdDev    | Gen0   | Gen1   | Allocated |
|----------------- |-----------:|------------:|----------:|-------:|-------:|----------:|
| Set              |   800.3 us | 1,370.08 us | 355.81 us | 2.0000 | 0.6667 |   7.59 KB |
| SetBuffered      |   705.4 us | 1,962.94 us | 303.77 us | 2.0000 | 0.6667 |   7.54 KB |
| SetAsync         |   713.3 us | 1,696.50 us | 262.54 us | 1.3333 | 0.3333 |   7.27 KB |
| SetBufferedAsync | 1,394.2 us | 2,729.99 us | 708.97 us | 1.3333 | 0.3333 |    7.3 KB |
| Get              |   145.0 us |    29.58 us |   7.68 us | 4.6667 |      - |  13.28 KB |
| GetBuffered      |   139.2 us |    38.01 us |   9.87 us | 3.0000 | 0.6667 |   9.28 KB |
| GetAsync         |   174.3 us |     9.25 us |   1.43 us | 4.6667 | 1.0000 |  15.13 KB |
| GetBufferedAsync |   171.0 us |    10.42 us |   1.61 us | 2.6667 | 0.3333 |  11.17 KB |

> [!Warning]
> - Size limits are eventual, not strict.
> - Write methods publishes without checking MaxTotalSize or MaxEntries
> - Eviction only acts on its periodic scan. LRU policy only.

### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)
| Method           | Mean       | Error      | StdDev     | Gen0   | Gen1   | Allocated |
|----------------- |-----------:|-----------:|-----------:|-------:|-------:|----------:|
| Set              | 2,866.5 us | 4,975.1 us | 1,292.0 us | 0.3333 |      - |   1.53 KB |
| SetAsync         | 2,290.8 us | 2,441.2 us |   634.0 us | 0.3333 |      - |   1.37 KB |
| Get              | 1,753.1 us |   523.9 us |   136.1 us | 1.3333 | 0.3333 |   5.52 KB |
| GetAsync         | 1,500.5 us |   424.8 us |   110.3 us | 1.3333 |      - |   5.77 KB |

> [!Warning]
> - Doesn't implement `IBufferDistributedCache`.
> - Doesn't enforce a disk usage limit or maximum cache size.

### [LiteDb.Extensions.Caching](https://www.nuget.org/packages/LiteDb.Extensions.Caching)
| Method           | Mean        | Error        | StdDev     | Gen0    | Gen1   | Allocated |
|----------------- |------------:|-------------:|-----------:|--------:|-------:|----------:|
| Set              | 2,601.81 us |   765.681 us | 198.845 us | 15.3333 | 1.3333 |  64.49 KB |
| SetAsync         | 1,859.92 us | 4,618.719 us | 714.752 us | 15.6667 | 2.3333 |  67.65 KB |
| Get              |    19.57 us |     3.071 us |   0.475 us |  5.6667 | 0.6667 |  23.74 KB |
| GetAsync         |    18.38 us |     2.176 us |   0.565 us |  6.0000 | 0.6667 |  24.58 KB |

> [!Warning]
> - Doesn't implement `IBufferDistributedCache`.
> - Doesn't enforce a disk usage limit or maximum cache size.

### [Caching.FileBackedDistributedCache](https://www.nuget.org/packages/Caching.FileBackedDistributedCache)
| Method           | Mean      | Error        | StdDev     | Gen0   | Gen1   | Allocated |
|----------------- |----------:|-------------:|-----------:|-------:|-------:|----------:|
| Set              | 763.84 us | 3,096.366 us | 479.166 us | 2.0000 | 0.3333 |   8.41 KB |
| SetAsync         | 631.92 us | 1,329.146 us | 345.175 us | 2.6667 |      - |   9.46 KB |
| Get              |  71.58 us |     7.238 us |   1.880 us | 3.6667 | 0.3333 |  11.85 KB |
| GetAsync         |  81.93 us |    12.656 us |   3.287 us | 3.6667 | 0.6667 |  13.08 KB |

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