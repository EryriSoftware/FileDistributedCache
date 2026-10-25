# Eryri.FileDistributedCache

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/Eryri.FileDistributedCache.svg)](https://www.nuget.org/packages/Eryri.FileDistributedCache)
[![NuGet downloads](https://img.shields.io/nuget/dt/Eryri.FileDistributedCache.svg)](https://img.shields.io/nuget/dt/Eryri.FileDistributedCache)
[![License](https://img.shields.io/github/license/EryriSoftware/FileDistributedCache)](https://github.com/EryriSoftware/FileDistributedCache/blob/main/LICENSE)

**A bounded, local disk-backed cache. No Redis. No cache server.**

Eryri.FileDistributedCache implements the standard .NET `IDistributedCache` and `IBufferDistributedCache` APIs while storing cache values on the local filesystem. It gives one application process more cache capacity than memory alone, with configurable size limits, expiration, eviction policies, and optional recovery across restarts.

Use it as:

- a **standalone local cache** when your application needs fast, bounded filesystem storage without operating another service; or
- a **`HybridCache` secondary tier** behind the in-memory local tier.

```bash
dotnet add package Eryri.FileDistributedCache
```

> **Important:** This is local, single-process storage. With a configured persistent directory, it can provide durable local storage across normal process restarts. It is not a shared Redis replacement, a cross-replica store, or a backup system.

## Why use Eryri?

| Need | Eryri.FileDistributedCache |
| --- | --- |
| More cache capacity without keeping every value in managed memory | Filesystem-backed values |
| A predictable cache budget | Byte-based size limit and automatic eviction |
| No additional infrastructure | Runs in the application process |
| Standard .NET integration | `IDistributedCache`, `IBufferDistributedCache`, dependency injection, and `HybridCache` |
| Durable local storage without an external service | Persistent directory with write-ahead logging and snapshots |
| Different workload strategies | LRU, LFU, FIFO, and TTL eviction |
| Shared data between replicas | **Not supported**; use a shared cache service |

Compared with an in-memory cache, Eryri trades some latency for substantially more capacity and optional restart recovery. Compared with SQLite or another embedded database cache, it provides standard .NET cache abstractions and cache-specific eviction semantics without requiring application code to manage a database schema. Compared with Redis, it needs no service and has no network hop, but it is local to one process and cannot coordinate replicas.

## Choose it when

- A single process needs a cache larger than the memory budget you want to reserve for it.
- Values are expensive to retrieve or compute but remain recoverable from another source.
- You need a maximum cache size and an eviction policy rather than an unbounded directory.
- You want a local `HybridCache` secondary tier without deploying Redis or another service.
- You run offline, on a developer workstation, in a test environment, or in a deployment where a local persistent volume is available.

## Choose something else when

- Multiple processes, containers, or application replicas must read and write the same entries. Use a shared backend such as Redis or a managed distributed cache.
- You need replication, transactions, backups, or a system of record across hosts. Use a database or shared durable storage system.
- You need automatic migration of an existing in-memory cache to disk under memory pressure. Configure `HybridCache` explicitly instead.
- You need semantic search, vector indexing, serialization, or model management. Eryri stores opaque cache values; pair it with the system that provides those capabilities.

## Quick start

Register Eryri with dependency injection and use the familiar distributed-cache API:

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
	.AddDistributedFileCache(options =>
	{
		options.SizeLimitGiB = 1;
		options.EvictionPolicy = EvictionPolicy.LRU;
	})
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

That is all that is required for an ephemeral local cache. No database, cache server, or separate process is needed.

## Use with `HybridCache`

Use Eryri as the filesystem-backed secondary tier while `HybridCache` manages the in-memory local tier:

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
	.AddDistributedFileCache(options =>
	{
		options.SizeLimitGiB = 2;
		options.EvictionPolicy = EvictionPolicy.LRU;
	})
	.AddHybridCache()
	.BuildServiceProvider();

var cache = services.GetRequiredService<HybridCache>();

var value = await cache.GetOrCreateAsync(
	"product:42",
	_ => ValueTask.FromResult("Cached product data"),
	new HybridCacheEntryOptions
	{
		Expiration = TimeSpan.FromMinutes(10),
		LocalCacheExpiration = TimeSpan.FromSeconds(5)
	});
```

The in-memory entry expires after five seconds, while the overall entry expires after ten minutes. `HybridCache` does not automatically spill arbitrary in-memory entries to disk; both tiers are configured local caches.

## AI and local data workloads

Eryri is useful for AI applications that repeatedly retrieve or compute data locally and can safely rebuild it after a cache miss:

- **LLM response caching:** cache deterministic prompt results using a key that includes the model, relevant settings, prompt version, and input hash.
- **Embedding and retrieval caching:** cache serialized embeddings, chunk lookups, and retrieved context when the underlying documents or embedding model version are part of the key.
- **Local model metadata and artifacts:** cache downloaded metadata or derived files when the deployment has a suitable persistent volume and the source can be checked again.
- **Agent and tool-call acceleration:** cache idempotent, repeatable tool results with an expiration appropriate to the data freshness requirement.
- **Offline and development workflows:** reduce repeated network calls and model or document preparation during local development, evaluation, and constrained deployments.

```csharp
services.AddDistributedFileCache(options =>
{
	options.CacheDirectory = "/var/cache/my-agent";
	options.SizeLimitGiB = 10;
	options.EvictionPolicy = EvictionPolicy.LFU;
	options.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
});
```

Recommended cache-key inputs for AI workloads include the model/provider identifier, model version, prompt or pipeline version, relevant generation settings, document/index version, and a stable input hash. Do not use the cache as the source of truth for safety decisions, permissions, financial data, or other data that must be authoritative. Eryri does not provide vector search, embedding generation, prompt templating, or cross-process coordination.

## Capacity and eviction

Set a maximum cache size in bytes, mebibytes, or gibibytes:

```csharp
services.AddDistributedFileCache(options =>
{
	options.SizeLimitBytes = 1024L * 1024 * 1024; // 1 GiB
	options.EvictionPolicy = EvictionPolicy.LRU;
});
```

The size limit applies to storage managed by this cache. It does not reserve disk space or account for unrelated files, filesystem metadata, WAL files, or other workloads. Leave appropriate free-space headroom and monitor the volume used by the application.

| Policy | Behavior | Suitable for |
| --- | --- | --- |
| `LRU` | Evicts least recently used entries | General-purpose workloads where recent access predicts reuse |
| `LFU` | Evicts least frequently used entries | Stable hot sets and repeated AI/model or reference data |
| `FIFO` | Evicts oldest entries first | Simple insertion-order retention |
| `TTL` | Prioritizes entries by remaining lifetime | Workloads where freshness and expiration should influence capacity eviction |

Capacity eviction is separate from expiration. An entry can expire regardless of the selected capacity policy. Expired entries are cleaned up automatically, and a write can evict existing entries to make room for a new value.

An individual value larger than the configured size limit cannot be made to fit by eviction; size limits are intended to bound normal cache occupancy and should be paired with application-level payload limits when strict admission control is required.

To request proactive compaction through the package-specific API:

```csharp
var fileCache = services.GetRequiredService<IFileDistributedCache>();
fileCache.Compact(0.10m); // request removal of at least 10% of entries
```

## Expiration

Use the standard `DistributedCacheEntryOptions` API:

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

The entry expires after one hour at the latest, or after ten minutes without access, whichever occurs first. You can configure defaults for entries that do not specify an expiration. Treat cache misses as normal: retrieve or recompute the value from its source of truth and cache it again.

Entry-level expiration settings take precedence over the corresponding `FileCacheOptions` defaults. If no entry-level or configured default expiration is present, the value has no time-based expiration and remains subject to capacity eviction.

## Persistence and operational reliability

By default, the cache uses a temporary directory that is removed during normal shutdown. Configure `CacheDirectory` to use Eryri as durable local storage whose values can be recovered after normal process restarts:

```csharp
services.AddDistributedFileCache(options =>
{
	options.CacheDirectory = "/var/cache/myapp";
	options.SizeLimitGiB = 1;
	options.EvictionPolicy = EvictionPolicy.LRU;
});
```

For containers, mount a suitable persistent volume at the configured path. The application identity needs read, write, create, rename, and delete permissions. Snapshots and write-ahead-log checkpointing are managed internally; `SnapshotInterval` controls the snapshot interval when persistence is enabled.

This persistence mode is durable local storage, but it is not replicated or backed up automatically. A process crash or forced termination can leave the latest write subject to the filesystem and WAL durability guarantees. Disk failure, volume deletion, permission changes, corruption, expiration, or configured capacity eviction can still remove or make values unavailable. Use a database or backup system when you need transactions, replication, independent recovery, or guaranteed retention.

Eryri therefore supports two valid roles: a bounded cache for recoverable values, and a durable local store for values whose durability requirements fit a single persistent volume and the configured expiration and eviction policy.

Read failures and missing files are treated as cache misses rather than allowing an unavailable cached value to become application data. Write and cleanup failures are logged, but filesystem capacity and permissions remain deployment responsibilities. Monitor the persistent volume, reserve free space, and include the cache directory in your backup and recovery plan when the stored values are durable application data.

## Configuration

| Option | Description |
| --- | --- |
| `SizeLimitBytes` | Maximum configured cache size in bytes. |
| `SizeLimitMiB` | Maximum configured cache size in MiB. |
| `SizeLimitGiB` | Maximum configured cache size in GiB. |
| `EvictionPolicy` | `LRU`, `LFU`, `FIFO`, or `TTL`. Defaults to `LRU`. |
| `DefaultSlidingExpiration` | Default sliding expiration for entries without one. |
| `DefaultAbsoluteExpirationRelativeToNow` | Default absolute expiration for entries without one. |
| `CacheDirectory` | Cache directory. Omit for an ephemeral temporary directory. |
| `SnapshotInterval` | Interval for compacting the WAL into a recovery snapshot. |

## Performance

Filesystem performance depends on the operating system, filesystem, storage medium, payload size, concurrency, cache state, and durability configuration. The repository contains repeatable BenchmarkDotNet coverage in [`BenchmarkTests.cs`](https://github.com/EryriSoftware/FileDistributedCache/blob/main/src/Extensions.Caching.FileSystem.Tests/Performance/BenchmarkTests.cs).

The benchmark suite compares several `IDistributedCache` implementations and exercises both unconstrained and capacity-constrained Eryri configurations. Run the benchmarks on your target hardware and workload before making capacity or latency commitments. Published numbers are directional evidence, not a performance guarantee.

The benchmark workload currently uses 4 KiB payloads, 10,000 initial entries, with 100 parallel operations.

### Published results

The following results are the supplied BenchmarkDotNet output for the workload described above. Use these figures as directional evidence and rerun the benchmarks on the target environment before making capacity or latency commitments.

Times are in microseconds (`us`). Allocated memory is per operation. `1 ms = 1,000 us` and `1 us = 1,000 ns`.

#### Eryri.FileDistributedCache - unbounded
| Method                      | Mean        | Error       | StdDev      | Median      | Min         | Allocated |
|---------------------------- |------------:|------------:|------------:|------------:|------------:|----------:|
| SetNewKey                   |  48.6698 us |   1.6104 us |   4.4623 us |  47.3963 us |  41.4725 us |    1647 B |
| SetNewKeyBuffered           |  46.3539 us |   0.9470 us |   2.6084 us |  46.4723 us |  41.7333 us |    1522 B |
| SetNewKeyAsync              |  45.7419 us |   1.4533 us |   4.0272 us |  44.6554 us |  39.9933 us |    1917 B |
| SetNewKeyBufferedAsync      |  45.3707 us |   1.6728 us |   4.6074 us |  44.2067 us |  38.1356 us |    2256 B |
| SetExistingKey              |  95.3772 us |   9.1494 us |  26.8337 us | 102.1533 us |  49.7350 us |    1345 B |
| SetExistingKeyBuffered      | 100.0465 us |   9.1987 us |  27.1225 us | 106.9369 us |  54.5908 us |    1448 B |
| SetExistingKeyAsync         |  92.6513 us |   7.5440 us |  22.2438 us |  96.0123 us |  50.9235 us |    1806 B |
| SetExistingKeyBufferedAsync |  78.1942 us |   7.2130 us |  21.2678 us |  75.4123 us |  50.9038 us |    2225 B |
| Get                         |  18.2335 us |   0.6381 us |   1.8512 us |  17.8342 us |  13.5563 us |    4938 B |
| GetBuffered                 |  18.2412 us |   0.5263 us |   1.5185 us |  18.0954 us |  14.1256 us |    1270 B |
| GetAsync                    |  18.4734 us |   0.6217 us |   1.8330 us |  18.5760 us |  14.4652 us |    5439 B |
| GetBufferedAsync            |  18.5885 us |   0.6490 us |   1.9033 us |  18.3748 us |  14.3119 us |    1664 B |
| GetMiss                     |   0.1409 us |   0.0116 us |   0.0338 us |   0.1350 us |   0.0921 us |      28 B |
| GetBufferedMiss             |   0.1995 us |   0.0135 us |   0.0384 us |   0.1908 us |   0.1487 us |      62 B |
| GetMissAsync                |   0.4891 us |   0.0480 us |   0.1378 us |   0.4854 us |   0.2779 us |      26 B |
| GetBufferedMissAsync        |   0.5200 us |   0.0516 us |   0.1489 us |   0.4858 us |   0.3094 us |      58 B |
| SetGetExistingKeyAsync      | 112.2482 us |  22.4940 us |  57.6608 us | 112.9388 us |  55.5738 us |    7193 B |

#### Eryri.FileDistributedCache - capacity constrained

This run measures Eryri with capacity enforcement and eviction enabled. It is not directly equivalent to the unbounded run because the cache performs additional capacity work.

| Method                      | Mean        | Error       | StdDev      | Median      | Min         | Allocated |
|---------------------------- |------------:|------------:|------------:|------------:|------------:|----------:|
| SetNewKey                   |  77.1885 us |   4.6616 us |  13.2242 us |  72.7521 us |  59.9237 us |    1549 B |
| SetNewKeyBuffered           |  82.6619 us |   6.0598 us |  17.4839 us |  75.8871 us |  59.0633 us |    1741 B |
| SetNewKeyAsync              |  68.9492 us |   3.7078 us |  10.4580 us |  65.7196 us |  55.9146 us |    2094 B |
| SetNewKeyBufferedAsync      |  82.2422 us |   7.0511 us |  20.5685 us |  75.1725 us |  58.1585 us |    2448 B |
| SetExistingKey              |  96.9676 us |   7.3365 us |  21.4008 us |  98.4587 us |  49.1331 us |    1329 B |
| SetExistingKeyBuffered      |  96.8690 us |   8.5862 us |  25.1819 us | 101.2867 us |  51.3038 us |    1473 B |
| SetExistingKeyAsync         |  87.6236 us |   9.0720 us |  26.7491 us |  89.2498 us |  48.5104 us |    1806 B |

#### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean       | Error     | StdDev     | Median     | Min        | Allocated |
|---------------------------- |-----------:|----------:|-----------:|-----------:|-----------:|----------:|
| SetNewKey                   | 130.211 us | 7.4796 us | 21.5803 us | 124.326 us |  76.633 us |   6.77 KB |
| SetNewKeyBuffered           | 134.180 us | 8.6013 us | 25.2263 us | 128.971 us |  79.498 us |   6.77 KB |
| SetNewKeyAsync              | 111.880 us | 8.5814 us | 24.7592 us | 105.423 us |  70.647 us |   6.64 KB |
| SetNewKeyBufferedAsync      | 115.533 us | 7.5585 us | 22.2864 us | 114.005 us |  72.098 us |   6.64 KB |
| SetExistingKey              | 306.386 us | 6.0879 us | 10.3377 us | 303.018 us | 293.998 us |   6.63 KB |
| SetExistingKeyBuffered      | 304.744 us | 6.0021 us |  5.3207 us | 304.901 us | 292.319 us |   6.63 KB |
| SetExistingKeyAsync         | 291.354 us | 5.7902 us |  8.1171 us | 289.830 us | 274.481 us |   6.79 KB |
| SetExistingKeyBufferedAsync | 324.663 us | 9.6867 us | 28.1028 us | 318.774 us | 277.895 us |    6.8 KB |
| Get                         |  54.035 us | 3.4810 us | 10.2639 us |  58.090 us |  31.043 us |   9.07 KB |
| GetBuffered                 |  54.310 us | 3.4439 us | 10.1543 us |  57.022 us |  30.216 us |   5.08 KB |
| GetAsync                    |  51.617 us | 3.6029 us | 10.6233 us |  56.920 us |  30.297 us |   9.76 KB |
| GetBufferedAsync            |  56.638 us | 3.2759 us |  9.5041 us |  59.155 us |  31.245 us |    5.8 KB |
| GetMiss                     |   5.089 us | 0.1733 us |  0.5056 us |   5.025 us |   4.104 us |   2.16 KB |
| GetBufferedMiss             |   4.855 us | 0.1669 us |  0.4922 us |   4.926 us |   3.668 us |   2.19 KB |
| GetMissAsync                |   5.077 us | 0.1573 us |  0.4612 us |   5.086 us |   4.072 us |   2.13 KB |
| GetBufferedMissAsync        |   5.171 us | 0.1564 us |  0.4612 us |   5.144 us |   3.766 us |   2.16 KB |
| SetGetExistingKeyAsync      | 318.364 us | 6.2585 us | 11.6005 us | 316.850 us | 296.212 us |  16.52 KB |

> The comparison implementations do not necessarily provide the same feature set. In particular, their capacity behavior and eviction semantics differ; compare the capabilities you need, not only the mean latency.

#### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean        | Error      | StdDev       | Median      | Min         | Allocated |
|---------------------------- |------------:|-----------:|-------------:|------------:|------------:|----------:|
| SetNewKey                   |   119.10 us |  17.747 us |    52.327 us |   144.82 us |    52.35 us |   1.46 KB |
| SetNewKeyAsync              |   118.36 us |  17.280 us |    50.949 us |   149.83 us |    51.33 us |   1.36 KB |
| SetExistingKey              |    46.75 us |   1.234 us |     3.637 us |    45.82 us |    38.02 us |   1.31 KB |
| SetExistingKeyAsync         |    47.19 us |   1.114 us |     3.283 us |    45.56 us |    42.24 us |   1.27 KB |
| Get                         | 2,581.55 us | 340.981 us | 1,005.391 us | 3,046.15 us | 1,520.98 us |   5.55 KB |
| GetAsync                    | 1,544.69 us |  16.929 us |    15.836 us | 1,542.57 us | 1,526.09 us |   5.78 KB |
| GetMiss                     |    26.19 us |   0.471 us |     0.952 us |    26.16 us |    24.54 us |   1.49 KB |
| GetMissAsync                |    29.02 us |   0.727 us |     2.132 us |    28.83 us |    25.56 us |   1.48 KB |
| SetGetExistingKeyAsync      | 1,580.09 us |  31.381 us |    46.970 us | 1,564.36 us | 1,523.60 us |   7.02 KB |

> NeoSmart.Caching.Sqlite did not implement `IBufferDistributedCache` in this benchmark and did not provide a configurable disk-usage or maximum-cache-size limit in the reviewed configuration. These are capability differences, not just performance differences.

#### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

| Method                      | Mean        | Error      | StdDev      | Median      | Min         | Gen0    | Allocated |
|---------------------------- |------------:|-----------:|------------:|------------:|------------:|--------:|----------:|
| SetNewKey                   | 306.1756 us | 21.0946 us |  60.1841 us | 288.0392 us | 173.6163 us | 10.0000 |  130938 B |
| SetNewKeyAsync              |          NA |         NA |          NA |          NA |          NA |      NA |        NA |
| SetExistingKey              | 311.3362 us | 20.9007 us |  59.2919 us | 295.4129 us | 174.9483 us | 10.0000 |  132623 B |
| SetExistingKeyAsync         | 349.8884 us | 18.8166 us |  55.4813 us | 347.3438 us | 212.4204 us | 10.0000 |  128895 B |
| Get                         |          NA |         NA |          NA |          NA |          NA |      NA |        NA |
| GetAsync                    |          NA |         NA |          NA |          NA |          NA |      NA |        NA |
| GetMiss                     |          NA |         NA |          NA |          NA |          NA |      NA |        NA |
| GetMissAsync                |   0.3530 us |  0.0125 us |   0.0362 us |   0.3573 us |   0.2704 us |       - |      58 B |
| SetGetExistingKeyAsync      |          NA |         NA |          NA |          NA |          NA |      NA |        NA |

> Net.DistributedFileStoreCache did not implement `IBufferDistributedCache` and threw a timeout exception for most operations because it took longer than 100ms. These are capability differences, not just performance differences.

## API and compatibility

- `IDistributedCache` support for existing .NET cache integrations.
- `IBufferDistributedCache` support for lower-allocation buffer-based access.
- Dependency injection registration through `AddDistributedFileCache`.
- `HybridCache` secondary-tier integration through the standard `IDistributedCache` registration.
- Native AOT and trimming compatibility are enabled in the package project.
- MIT licensed.

## Frequently asked questions

### Is this a local Redis alternative?

It is an alternative when one process needs a bounded local cache without a Redis server. It is not a protocol-compatible Redis server and does not provide shared state between replicas.

### Is it really distributed because it implements `IDistributedCache`?

No. `IDistributedCache` is the .NET abstraction used for integration. Eryri stores data in the local process and local filesystem only.

### Does persistence guarantee that every cached value survives?

No. Persistence supports recovery across normal restarts, but cache misses and data loss remain valid outcomes. Keep a source of truth.

### Does it automatically move memory-cache entries to disk?

No. Configure Eryri as the `IDistributedCache` secondary tier for `HybridCache`; it does not automatically spill arbitrary existing memory-cache entries to disk.

### Is it suitable for multiple application replicas?

Only as an independent cache per replica. Use a shared cache backend when replicas must observe the same entries.

