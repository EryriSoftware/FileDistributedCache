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
| Method                      | Mean        | Error         | StdDev     | Allocated |
|---------------------------- |------------:|--------------:|-----------:|----------:|
| SetNewKey                   | 127.8577 us |    51.1645 us |  2.8045 us |    1231 B |
| SetNewKeyBuffered           | 148.1137 us |   582.9216 us | 31.9519 us |    1387 B |
| SetNewKeyAsync              | 151.6980 us |   565.2675 us | 30.9842 us |    2029 B |
| SetNewKeyBufferedAsync      | 133.8997 us |   144.6389 us |  7.9281 us |    2346 B |
| SetExistingKey              | 140.8630 us |   179.5011 us |  9.8391 us |    1058 B |
| SetExistingKeyBuffered      | 129.7753 us |    80.5937 us |  4.4176 us |    1251 B |
| SetExistingKeyAsync         | 122.1133 us |    89.8002 us |  4.9223 us |    1795 B |
| SetExistingKeyBufferedAsync | 131.7057 us |    88.9324 us |  4.8747 us |    2140 B |
| Get                         |  97.2297 us |    92.5593 us |  5.0735 us |    4685 B |
| GetBuffered                 | 101.3107 us |    73.8722 us |  4.0492 us |     771 B |
| GetAsync                    | 111.8857 us |    76.9949 us |  4.2204 us |    5461 B |
| GetBufferedAsync            | 116.7687 us |    56.8852 us |  3.1181 us |    1677 B |
| GetMiss                     |   0.6262 us |     0.7965 us |  0.0437 us |      21 B |
| GetBufferedMiss             |   0.5173 us |     2.1007 us |  0.1151 us |      54 B |
| GetMissAsync                |   0.9070 us |     0.5259 us |  0.0288 us |      26 B |
| GetBufferedMissAsync        |   1.6912 us |     7.7566 us |  0.4252 us |      58 B |
| SetGetExistingKeyAsync      | 168.5353 us |   357.6907 us | 19.6062 us |    7408 B |

#### Eryri.FileDistributedCache - capacity constrained

This run measures Eryri with capacity enforcement and eviction enabled. It is not directly equivalent to the unbounded run because the cache performs additional capacity work.

| Method                      | Mean        | Error         | StdDev     | Allocated |
|---------------------------- |------------:|--------------:|-----------:|----------:|
| SetExistingKey              | 134.4903 us |   365.6017 us | 20.0399 us |    1046 B |
| SetExistingKeyBuffered      | 145.3803 us |   872.9513 us | 47.8494 us |    1251 B |
| SetExistingKeyAsync         | 122.0713 us |    61.7802 us |  3.3864 us |    1788 B |
| SetExistingKeyBufferedAsync | 139.5760 us |   124.2337 us |  6.8097 us |    2138 B |

#### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean        | Error         | StdDev       | Median      | Allocated |
|---------------------------- |------------:|--------------:|-------------:|------------:|----------:|
| SetNewKey                   |   600.84 us |    679.314 us |    37.235 us |   582.08 us |   7.03 KB |
| SetNewKeyBuffered           |   553.11 us |    461.902 us |    25.318 us |   543.90 us |   7.24 KB |
| SetNewKeyAsync              | 1,936.92 us | 41,692.637 us | 2,285.314 us |   622.67 us |   7.81 KB |
| SetNewKeyBufferedAsync      |   631.99 us |    369.033 us |    20.228 us |   632.39 us |   7.89 KB |
| SetExistingKey              | 1,413.34 us |  2,387.776 us |   130.882 us | 1,343.50 us |    9.5 KB |
| SetExistingKeyBuffered      | 1,514.94 us |  7,037.878 us |   385.770 us | 1,305.36 us |   9.46 KB |
| SetExistingKeyAsync         | 1,345.91 us |    909.500 us |    49.853 us | 1,331.15 us |    9.5 KB |
| SetExistingKeyBufferedAsync | 1,344.86 us |    300.017 us |    16.445 us | 1,348.14 us |   9.57 KB |
| Get                         |    81.45 us |     86.728 us |     4.754 us |    82.54 us |   9.13 KB |
| GetBuffered                 |    77.04 us |     50.721 us |     2.780 us |    76.86 us |   5.15 KB |
| GetAsync                    |   129.79 us |     77.526 us |     4.249 us |   129.90 us |  10.19 KB |
| GetBufferedAsync            |   127.94 us |     46.536 us |     2.551 us |   127.50 us |   6.25 KB |
| GetMiss                     |    12.88 us |     28.610 us |     1.568 us |    12.28 us |    1.7 KB |
| GetBufferedMiss             |    13.10 us |      1.495 us |     0.082 us |    13.09 us |   1.73 KB |
| GetMissAsync                |    11.85 us |     10.957 us |     0.601 us |    12.12 us |   1.68 KB |
| GetBufferedMissAsync        |    12.38 us |     17.127 us |     0.939 us |    11.93 us |   1.71 KB |
| SetGetExistingKeyAsync      | 4,044.12 us | 77,955.145 us | 4,272.985 us | 1,615.15 us |  20.15 KB |

> The comparison implementations do not necessarily provide the same feature set. In particular, their capacity behavior and eviction semantics differ; compare the capabilities you need, not only the mean latency.

#### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean       | Error        | StdDev      | Median     | Allocated |
|---------------------------- |-----------:|-------------:|------------:|-----------:|----------:|
| SetNewKey                   | 1,995.3 us | 57,407.64 us | 3,146.71 us |   179.7 us |   1.44 KB |
| SetNewKeyAsync              | 1,391.3 us | 19,353.82 us | 1,060.85 us | 1,908.9 us |   1.35 KB |
| SetExistingKey              |   296.3 us |  5,041.82 us |   276.36 us |   144.1 us |   1.31 KB |
| SetExistingKeyAsync         |   292.8 us |  5,035.68 us |   276.02 us |   134.2 us |   1.27 KB |
| Get                         | 1,217.6 us | 16,468.37 us |   902.69 us | 1,705.6 us |   5.43 KB |
| GetAsync                    | 1,630.2 us |  1,806.96 us |    99.05 us | 1,676.1 us |   5.77 KB |
| GetMiss                     |   173.0 us |     60.60 us |     3.32 us |   173.6 us |   1.48 KB |
| GetMissAsync                |   184.0 us |     42.94 us |     2.35 us |   184.4 us |   1.47 KB |
| SetGetExistingKeyAsync      | 1,793.1 us |  3,054.11 us |   167.41 us | 1,722.6 us |   7.02 KB |

> NeoSmart.Caching.Sqlite did not implement `IBufferDistributedCache` in this benchmark and did not provide a configurable disk-usage or maximum-cache-size limit in the reviewed configuration. These are capability differences, not just performance differences.

#### [Net.DistributedFileStoreCache](https://www.nuget.org/packages/Net.DistributedFileStoreCache)

> Net.DistributedFileStoreCache did not implement `IBufferDistributedCache` and threw a timeout exception for every operation because it took longer than 100ms. These are capability differences, not just performance differences.

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

