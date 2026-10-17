# [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)

**More cache. Less RAM. No cache server.**

Cache large, reusable datasets on local disk without keeping the entire cache in memory or running a separate cache service. Eryri.FileDistributedCache is a bounded, filesystem-backed cache for .NET, with automatic eviction, expiration and optional persistence across process restarts.

Implements .NET's `IDistributedCache` and `IBufferDistributedCache` interfaces, so you can use familiar caching APIs with local filesystem storage.

**Key features**

- **Bounded storage:** Configure a byte limit and automatically evict entries to make room before admitting new writes.
- **Low memory overhead:** Store cache values on disk instead of retaining the entire cached dataset in managed memory.
- **Configurable eviction:** Choose `LRU`, `LFU`, `FIFO` or `TTL`-based eviction to suit your workload.
- **Automatic expiration:** Support absolute and sliding expiration, with background cleanup scheduled around upcoming expirations.
- **Optional persistence:** Preserve cache entries across process restarts using a write-ahead log and periodic snapshots.
- **Native .NET integration:** Support `IDistributedCache`, `IBufferDistributedCache` and `HybridCache` secondary-tier integration.
- **No additional service:** Use local disk without deploying Redis or another cache server.

Use an ephemeral cache for disposable data, or configure a persistent cache directory when you want entries to survive normal process restarts.

### Important: local, single-process cache

Despite implementing `IDistributedCache`, this package is **not a distributed cache backend**. Each process owns its own cache, and entries are not shared or synchronised across processes or application replicas.

Cached values should always be recoverable from their source of truth. Persistence helps avoid rebuilding the cache after routine restarts; it does not turn the cache into durable primary storage.

---
## Why use it?

- **Cache more without consuming more RAM.** Store large, reusable datasets on disk instead of retaining every cached value in memory. Ideal when the working set exceeds the memory budget you want to allocate to caching.
- **Keep cache growth under control.** Set a byte limit and automatically evict entries to make room before admitting new writes. Choose `LRU`, `LFU`, `FIFO` or `TTL`-based eviction to match your workload.
- **Remove expired data automatically.** Support absolute and sliding expiration, with background cleanup scheduled around upcoming expirations rather than relying solely on fixed-interval scans.
- **Use familiar .NET caching APIs.** Integrate through `IDistributedCache` or `IBufferDistributedCache`, with thread-safe access and Native AOT compatibility.
- **Avoid unnecessary infrastructure.** Give a single-process application more cache capacity without deploying and maintaining a separate cache server.
- **Recover cached data after restarts.** Configure a persistent cache directory to retain entries across normal process restarts, without keeping the entire cache index in memory.

**Ideal for:** APIs, background workers and services with large, reusable datasets, limited RAM and available local disk space.

**Not intended for:** sharing cache entries across application replicas or replacing a database as the source of truth. The configured cache limit governs the cache's own entries; it does not reserve disk space or protect the underlying filesystem from other workloads.

---
### Scope at a glance

| Requirement | Supported |
| --- | --- |
| Standard .NET caching APIs | `IDistributedCache`, `IBufferDistributedCache` |
| Filesystem-backed cache storage | Yes |
| Configurable cache-size limit | Yes; byte-based, with automatic capacity eviction |
| LRU, LFU, FIFO and TTL eviction | Yes |
| Absolute and sliding expiration | Yes; automatic background cleanup |
| Persistence across process restarts | Yes; configure a persistent cache directory |
| Native AOT compatibility | Yes |
| `HybridCache` secondary-tier integration | Yes |
| Shared cache across processes or replicas | No |
| Automatic spill from RAM to disk under memory pressure | No |

Eryri is designed for **local cache capacity, not distributed coordination**. Use it when a single process needs a bounded filesystem-backed cache; choose a shared backend when multiple application instances must access the same cached entries.

---
## Install and get started

Install the NuGet package:

```bash
dotnet add package Eryri.FileDistributedCache
```

Register the cache with dependency injection and use the standard `IDistributedCache` API:

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

That's it. No cache server, database or additional infrastructure required.

### Cache misses are normal

A cached value may be missing because it expired, was evicted to make room for another entry, or was lost when an ephemeral cache was shut down.

Always treat the cache as an optimisation: retrieve or recompute missing values from their source of truth, then cache the result again.

### Use with `HybridCache`

[Microsoft's `HybridCache`](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid) combines an in-memory cache with a secondary cache provider. Register Eryri as the `IDistributedCache` implementation to use local filesystem storage for that secondary tier.

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .AddHybridCache()
    .BuildServiceProvider();

var cache = services.GetRequiredService<HybridCache>();

var options = new HybridCacheEntryOptions
{
    Expiration = TimeSpan.FromMinutes(10),
    LocalCacheExpiration = TimeSpan.FromSeconds(5)
};

var value = await cache.GetOrCreateAsync(
    "product:42",
    _ => ValueTask.FromResult("Cached product data"),
    options);

Console.WriteLine(value);
```

Here, the in-memory entry has a five-second expiration, while the overall cache entry has a ten-minute expiration.

**Note:** Eryri provides the filesystem-backed secondary cache; `HybridCache` manages the caching tiers. This does not automatically spill in-memory entries to disk when RAM is exhausted. Both tiers remain local to the process.

---
## Expiration and capacity

Eryri.FileDistributedCache keeps cache storage bounded and removes expired entries automatically. Configure a storage limit, choose an eviction policy and let the cache manage its own capacity.

### Bounded storage

Set a maximum cache size in bytes. When additional space is required, the cache evicts entries according to its configured policy before admitting new writes.

```csharp
services.AddDistributedFileCache(options =>
{
    options.SizeLimitBytes = 1024L * 1024 * 1024; // 1 GiB
});
```

The size limit controls the cache's own storage, not total filesystem usage. It does not reserve disk space or account for unrelated files and workloads.

### Eviction policies

Choose the policy that best matches your workload:

| Policy | Behaviour | Best suited to |
|---|---|---|
| `LRU` | Evicts the least recently used entries | Workloads where recently accessed data is more likely to be reused |
| `LFU` | Evicts the least frequently used entries | Workloads with a stable set of frequently accessed items |
| `FIFO` | Evicts the oldest entries first, regardless of access frequency | Simple insertion-order eviction |
| `TTL` | Prioritises entries according to their time to live | Workloads where remaining lifetime should influence eviction |

The eviction policy determines which entries are removed when capacity is needed. Expiration is separate: an entry can expire regardless of the selected capacity policy.

### Automatic expiration

Use the standard .NET `DistributedCacheEntryOptions` API to configure absolute or sliding expiration.

```csharp
await cache.SetStringAsync(
    "product:42",
    "Cached product data",
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
        SlidingExpiration = TimeSpan.FromMinutes(10)
    });
```

With these settings, the entry expires after one hour at the latest, or after ten minutes without access, whichever occurs first.

Expired entries are cleaned up automatically. Background cleanup is scheduled around upcoming expirations, helping avoid unnecessary repeated full-cache scans.

### Reclaim space explicitly

When you need to reclaim cache capacity proactively, use `IFileDistributedCache.Compact(decimal percentage)` to request eviction of a proportion of the cache.

This is useful when you want to reduce cache occupancy without waiting for subsequent writes to require additional space.

## Configuration reference

Configure Eryri.FileDistributedCache through `AddDistributedFileCache`. The options control capacity, eviction, expiration and persistence.

| Option | Purpose |
|---|---|
| `EvictionPolicy` | Selects the policy used for capacity eviction and manual compaction: `LRU`, `LFU`, `FIFO` or `TTL`. |
| `SizeLimitBytes` | Sets the maximum configured cache size in bytes. |
| `SizeLimitMiB` | Sets the cache size limit in [mebibytes](https://simple.wikipedia.org/wiki/Mebibyte) (MiB). |
| `SizeLimitGiB` | Sets the cache size limit in [gibibytes](https://simple.wikipedia.org/wiki/Gibibyte) (GiB). |
| `DefaultSlidingExpiration` | Sets the default sliding expiration for entries that do not specify one. |
| `DefaultAbsoluteExpirationRelativeToNow` | Sets the default absolute expiration relative to the time an entry is added. |
| `CacheDirectory` | Specifies the cache directory. When omitted, the cache uses an ephemeral temporary directory that is deleted during normal shutdown. |
| `SnapshotInterval` | Sets how often the write-ahead log (WAL) is compacted into a snapshot when a persistent cache directory is configured. |

### Example

Configure a 1 GiB cache with least-recently-used eviction:

```csharp
services.AddDistributedFileCache(options =>
{
    options.SizeLimitGiB = 1;
    options.EvictionPolicy = EvictionPolicy.LRU;
});
```

To retain entries across normal process restarts, configure a persistent directory as well:

```csharp
services.AddDistributedFileCache(options =>
{
    options.CacheDirectory = "/var/cache/myapp";
    options.SizeLimitGiB = 1;
    options.EvictionPolicy = EvictionPolicy.LRU;
});
```

Choose a directory that the application can access and that persists across deployments or container restarts.

### Automatic expiration

Eryri schedules a timer for upcoming entry expirations and evicts expired entries when they become due. It does not rely on periodic full-cache expiration scans, so there is no cleanup-frequency option to configure.

Expiration and capacity eviction are separate mechanisms: expiration removes entries whose lifetime has ended, while the configured eviction policy determines which entries are removed when additional capacity is required.

---
### Performance and benchmarks

Eryri.FileDistributedCache is designed for low-latency filesystem-backed caching with bounded storage and minimal managed-memory overhead.

Benchmarks compare Eryri against alternative IDistributedCache implementations using consistent workloads, allowing you to evaluate read latency, write latency and the cost of maintaining a full cache.

The most meaningful benchmark is **sustained operation under realistic workloads with a full cache**. This measures the cost of maintaining the cache under continuous writes, rather than simply adding entries to an empty directory.

Since the other libraries do not evict entries before inserting new ones, all implementations were benchmarked with pre-populated entries and no configured size limit, ensuring a consistent comparison.

These results provide useful directional evidence, not a guarantee of performance across different machines or environments.

- PayloadSize=4KiB
- InitialEntries=10000
- ParallelOperations=100
- InvocationCount=30
- IterationCount=5
- UnrollFactor=1  
- WarmupCount=1

Tests located here: [BenchmarkTests.cs](https://github.com/EryriSoftware/FileDistributedCache/blob/main/src/Extensions.Caching.FileSystem.Tests/Performance/BenchmarkTests.cs)
> 1s == 1000ms, 1ms == 1000us, 1us == 1000ns

### [Eryri.FileDistributedCache](https://www.nuget.org/packages/Eryri.FileDistributedCache)
| Method                      | Mean        | Error       | StdDev     | Gen0   | Allocated |
|---------------------------- |------------:|------------:|-----------:|-------:|----------:|
| SetNewKey                   |  65.3670 us |  17.2505 us |  4.4799 us |      - |     968 B |
| SetNewKeyBuffered           |  59.9437 us |  10.3588 us |  2.6902 us |      - |    1198 B |
| SetNewKeyAsync              |  58.8167 us |   7.2776 us |  1.8900 us |      - |    1464 B |
| SetNewKeyBufferedAsync      |  58.6129 us |   8.9358 us |  2.3206 us |      - |    1848 B |
| SetExistingKey              | 140.9930 us | 162.1063 us | 42.0985 us |      - |     959 B |
| SetExistingKeyBuffered      | 113.4463 us | 119.5436 us | 31.0451 us |      - |    1130 B |
| SetExistingKeyAsync         | 122.9913 us | 127.1685 us | 33.0252 us |      - |    1472 B |
| SetExistingKeyBufferedAsync | 123.0858 us | 125.8678 us | 32.6875 us |      - |    1878 B |
| Get                         |  42.7094 us |   3.9105 us |  1.0156 us | 0.6667 |    4606 B |
| GetBuffered                 |  42.9815 us |   8.2967 us |  1.2839 us |      - |     659 B |
| GetAsync                    |  43.2117 us |   2.2866 us |  0.5938 us | 0.6667 |    5125 B |
| GetBufferedAsync            |  41.8401 us |   1.7770 us |  0.2750 us |      - |    1339 B |
| GetMiss                     |   0.1694 us |   0.0126 us |  0.0033 us |      - |      33 B |
| GetBufferedMiss             |   0.2952 us |   0.1079 us |  0.0167 us |      - |      75 B |
| GetMissAsync                |   0.4277 us |   0.1529 us |  0.0397 us |      - |      18 B |
| GetBufferedMissAsync        |   0.4744 us |   0.1044 us |  0.0271 us |      - |      51 B |
| SetGetExistingKeyAsync      | 147.0018 us | 142.0474 us | 36.8893 us | 1.0000 |    6641 B |

Here is an additional test run where a cache limit is applied and is running at capacity, showing the benchmark for when items must be evicted before adding new entries.

| Method                      | Mean        | Error       | StdDev     | Gen0   | Allocated |
|---------------------------- |------------:|------------:|-----------:|-------:|----------:|
| SetNewKey                   |  97.6610 us |  32.4049 us |  5.0147 us |      - |    1153 B |
| SetNewKeyBuffered           | 107.7574 us |  13.4834 us |  2.0866 us |      - |    1344 B |
| SetNewKeyAsync              |  98.9432 us |  24.9157 us |  3.8557 us |      - |    1702 B |
| SetNewKeyBufferedAsync      |  92.8770 us |  23.5679 us |  3.6472 us |      - |    2012 B |
| SetExistingKey              |  87.8364 us |  30.8385 us |  8.0086 us |      - |    1218 B |
| SetExistingKeyBuffered      |  87.9151 us |  62.3318 us |  9.6459 us |      - |    1398 B |
| SetExistingKeyAsync         | 111.8476 us |  95.2294 us | 24.7308 us |      - |    1499 B |
| SetExistingKeyBufferedAsync | 109.8693 us | 139.6308 us | 36.2617 us |      - |    1855 B |


### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)
| Method                      | Mean       | Error       | StdDev     | Gen0   | Gen1   | Allocated |
|---------------------------- |-----------:|------------:|-----------:|-------:|-------:|----------:|
| SetNewKey                   | 114.975 us |  25.5473 us |  6.6345 us | 0.6667 | 0.3333 |   6.72 KB |
| SetNewKeyBuffered           | 163.829 us |  52.4407 us |  8.1153 us | 0.6667 | 0.3333 |   6.73 KB |
| SetNewKeyAsync              | 122.034 us |  27.8208 us |  7.2250 us | 0.6667 | 0.3333 |   6.63 KB |
| SetNewKeyBufferedAsync      | 114.553 us |  32.7253 us |  5.0643 us | 0.6667 | 0.3333 |   6.64 KB |
| SetExistingKey              | 282.031 us | 138.2415 us | 35.9009 us | 0.6667 |      - |   6.61 KB |
| SetExistingKeyBuffered      | 249.783 us |  10.0769 us |  2.6169 us | 0.6667 |      - |   6.61 KB |
| SetExistingKeyAsync         | 253.418 us |  20.2011 us |  5.2462 us | 0.6667 |      - |   6.79 KB |
| SetExistingKeyBufferedAsync | 261.877 us |  65.2737 us | 10.1012 us | 0.6667 |      - |   6.79 KB |
| Get                         |  69.996 us |  20.4176 us |  5.3024 us | 2.3333 | 0.6667 |  13.31 KB |
| GetBuffered                 |  71.877 us |  23.4960 us |  6.1018 us | 1.3333 | 0.3333 |   9.29 KB |
| GetAsync                    |  73.393 us |  12.7388 us |  3.3082 us | 2.3333 | 0.6667 |  14.76 KB |
| GetBufferedAsync            |  68.692 us |  12.4676 us |  1.9294 us | 1.3333 | 0.3333 |   10.8 KB |
| GetMiss                     |   3.849 us |   0.4705 us |  0.1222 us |      - |      - |   2.16 KB |
| GetBufferedMiss             |   3.865 us |   0.6542 us |  0.1012 us |      - |      - |   2.19 KB |
| GetMissAsync                |   4.082 us |   0.1387 us |  0.0215 us |      - |      - |   2.12 KB |
| GetBufferedMissAsync        |   3.754 us |   0.7176 us |  0.1864 us |      - |      - |   2.15 KB |
| SetGetExistingKeyAsync      | 272.297 us |   8.1186 us |  2.1084 us | 3.0000 | 1.0000 |  21.54 KB |

> [!Warning]
> - Capacity enforcement is eventual rather than strict.
> - Write operations do not check MaxTotalSize or MaxEntries before publishing entries.
> - Eviction is performed by a periodic scan; LRU is the supported policy in the reviewed implementation.

### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)
| Method                      | Mean        | Error      | StdDev     | Gen0   | Allocated |
|---------------------------- |------------:|-----------:|-----------:|-------:|----------:|
| SetNewKey                   |   124.60 us |  19.429 us |   5.046 us |      - |   1.49 KB |
| SetNewKeyAsync              |   126.63 us |  12.050 us |   3.129 us |      - |   1.38 KB |
| SetExistingKey              |    48.81 us |   2.834 us |   0.736 us |      - |   1.33 KB |
| SetExistingKeyAsync         |    49.36 us |   4.517 us |   1.173 us |      - |   1.29 KB |
| Get                         | 2,147.39 us | 474.658 us | 123.267 us | 0.6667 |   5.55 KB |
| GetAsync                    | 1,547.56 us |  93.061 us |  24.168 us | 0.6667 |   5.78 KB |
| GetMiss                     |    45.91 us |   2.954 us |   0.767 us |      - |   1.52 KB |
| GetMissAsync                |    50.34 us |   2.605 us |   0.677 us |      - |   1.47 KB |
| SetGetExistingKeyAsync      | 1,463.27 us | 112.902 us |  17.472 us | 0.6667 |   7.04 KB |

> [!Warning]
> - Does not implement IBufferDistributedCache.
> - The reviewed implementation does not provide a configurable disk-usage or maximum-cache-size limit.

### [LiteDb.Extensions.Caching](https://www.nuget.org/packages/LiteDb.Extensions.Caching)

> [!Warning]
> - Does not implement IBufferDistributedCache.
> - The reviewed implementation does not provide a configurable disk-usage or maximum-cache-size limit.
> - Can not overwrite an existing key without deleting it first.

### [Caching.FileBackedDistributedCache](https://www.nuget.org/packages/Caching.FileBackedDistributedCache)

> [!Warning]
> - Does not implement IBufferDistributedCache.
> - The reviewed implementation does not provide a configurable disk-usage or maximum-cache-size limit.
> - Expired entries are treated as cache misses, but their files are not automatically deleted; disk usage can therefore grow over time.
> - Requires Windows OS.

### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

> [!Warning]
> - Does not implement IBufferDistributedCache.
> - Retains the entire cache in-memory. The filesystem is used as persistance/distribution mechanism.
> - Does not support SlidingExpiration.
