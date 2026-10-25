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

The benchmark workload currently uses 4 KiB payloads, 10,000 initial entries, 100 parallel operations, five iterations, and one warmup iteration.

### Published results

The following results are the supplied BenchmarkDotNet output for the workload described above. Use these figures as directional evidence and rerun the benchmarks on the target environment before making capacity or latency commitments.

Times are in microseconds (`us`). Allocated memory is per operation. `1 ms = 1,000 us` and `1 us = 1,000 ns`.

#### Eryri.FileDistributedCache - unbounded
| Method                      | Mean        | Error         | StdDev     | Median      | Allocated |
|---------------------------- |------------:|--------------:|-----------:|------------:|----------:|
| SetNewKey                   | 128.6680 us |   609.1186 us | 33.3878 us | 110.1160 us |     970 B |
| SetNewKeyBuffered           | 109.4972 us |    91.3156 us |  5.0053 us | 110.6485 us |    1141 B |
| SetNewKeyAsync              | 130.6477 us | 1,080.4936 us | 59.2255 us |  96.8310 us |    1701 B |
| SetNewKeyBufferedAsync      | 104.4047 us |   330.6297 us | 18.1229 us |  96.0740 us |    2100 B |
| SetExistingKey              | 117.7540 us |    55.5900 us |  3.0471 us | 118.6700 us |     948 B |
| SetExistingKeyBuffered      | 116.0453 us |    52.6740 us |  2.8872 us | 116.3930 us |    1127 B |
| SetExistingKeyAsync         | 120.2565 us |    60.4085 us |  3.3112 us | 121.8735 us |    1700 B |
| SetExistingKeyBufferedAsync | 140.4607 us |   421.7799 us | 23.1192 us | 134.1980 us |    2090 B |
| Get                         |  79.1013 us |   122.1560 us |  6.6958 us |  81.5120 us |    4624 B |
| GetBuffered                 |  78.8573 us |    19.5853 us |  1.0735 us |  78.4690 us |     700 B |
| GetAsync                    |  88.5443 us |    62.2939 us |  3.4145 us |  86.5980 us |    5392 B |
| GetBufferedAsync            | 101.4777 us |    65.9420 us |  3.6145 us | 102.3590 us |    1624 B |
| GetMiss                     |   0.3053 us |     1.0528 us |  0.0577 us |   0.3330 us |      20 B |
| GetBufferedMiss             |   0.3307 us |     0.9716 us |  0.0533 us |   0.3150 us |      53 B |
| GetMissAsync                |   0.7970 us |     0.9994 us |  0.0548 us |   0.7810 us |      26 B |
| GetBufferedMissAsync        |   0.9683 us |     0.9706 us |  0.0532 us |   0.9630 us |      58 B |
| SetGetExistingKeyAsync      |          NA |            NA |         NA |          NA |        NA |

#### Eryri.FileDistributedCache - capacity constrained

This run measures Eryri with capacity enforcement and eviction enabled. It is not directly equivalent to the unbounded run because the cache performs additional capacity work.

| Method                      | Mean        | Error         | StdDev     | Median      | Allocated |
| SetExistingKey              | 114.2567 us |    56.9855 us |  3.1236 us | 114.2330 us |     961 B |
| SetExistingKeyBuffered      | 116.8650 us |    14.8156 us |  0.8121 us | 117.1240 us |    1123 B |
| SetExistingKeyAsync         | 127.6500 us |    71.8828 us |  3.9401 us | 125.7310 us |    1699 B |
| SetExistingKeyBufferedAsync | 123.9650 us |    63.8271 us |  3.4986 us | 125.2530 us |    2084 B |

#### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean         | Error        | StdDev      | Median       | Allocated |
|---------------------------- |-------------:|-------------:|------------:|-------------:|----------:|
| SetNewKey                   |   511.102 us |    26.988 us |   1.4793 us |   511.589 us |   6.83 KB |
| SetNewKeyBuffered           |   546.786 us | 1,466.857 us |  80.4034 us |   511.071 us |   6.83 KB |
| SetNewKeyAsync              |   475.618 us |   476.577 us |  26.1228 us |   479.763 us |   6.79 KB |
| SetNewKeyBufferedAsync      |   495.308 us |   429.634 us |  23.5497 us |   507.655 us |   6.79 KB |
| SetExistingKey              | 1,079.283 us |   323.836 us |  17.7505 us | 1,072.975 us |   6.77 KB |
| SetExistingKeyBuffered      | 1,094.030 us |   531.464 us |  29.1313 us | 1,108.784 us |   6.78 KB |
| SetExistingKeyAsync         | 1,131.808 us |   295.456 us |  16.1949 us | 1,132.663 us |   6.93 KB |
| SetExistingKeyBufferedAsync | 1,126.044 us |   318.599 us |  17.4635 us | 1,132.287 us |   6.94 KB |
| Get                         |   499.759 us | 9,425.709 us | 516.6549 us |   208.228 us |  13.25 KB |
| GetBuffered                 |   192.245 us |    41.912 us |   2.2973 us |   190.972 us |   9.26 KB |
| GetAsync                    |   214.876 us |    83.648 us |   4.5850 us |   212.232 us |  15.12 KB |
| GetBufferedAsync            |   213.108 us |    89.275 us |   4.8935 us |   215.149 us |  11.16 KB |
| GetMiss                     |     9.682 us |    12.795 us |   0.7013 us |     9.929 us |   1.96 KB |
| GetBufferedMiss             |     9.129 us |    10.069 us |   0.5519 us |     9.175 us |      2 KB |
| GetMissAsync                |     8.937 us |     1.431 us |   0.0784 us |     8.928 us |   1.96 KB |
| GetBufferedMissAsync        |     9.439 us |     2.647 us |   0.1451 us |     9.362 us |   1.99 KB |
| SetGetExistingKeyAsync      | 1,492.417 us | 1,547.310 us |  84.8133 us | 1,463.576 us |  22.04 KB |

> The comparison implementations do not necessarily provide the same feature set. In particular, their capacity behavior and eviction semantics differ; compare the capabilities you need, not only the mean latency.

#### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)

| Method                      | Mean        | Error        | StdDev     | Median      | Allocated |
|---------------------------- |------------:|-------------:|-----------:|------------:|----------:|
| SetNewKey                   |   953.94 us | 14,145.67 us | 775.372 us | 1,043.38 us |   1.51 KB |
| SetNewKeyAsync              | 1,047.42 us | 15,216.08 us | 834.045 us | 1,267.16 us |   1.42 KB |
| SetExistingKey              |   162.69 us |  2,402.43 us | 131.685 us |    89.75 us |   1.42 KB |
| SetExistingKeyAsync         |   242.89 us |  4,674.20 us | 256.209 us |    95.93 us |   1.34 KB |
| Get                         | 2,663.19 us | 17,747.29 us | 972.789 us | 3,205.80 us |   5.49 KB |
| GetAsync                    | 1,637.44 us |  1,200.38 us |  65.797 us | 1,663.46 us |   5.77 KB |
| GetMiss                     |    75.68 us |     16.11 us |   0.883 us |    75.38 us |   1.51 KB |
| GetMissAsync                |    74.13 us |     17.62 us |   0.966 us |    73.62 us |   1.54 KB |
| SetGetExistingKeyAsync      | 2,215.46 us | 16,542.27 us | 906.738 us | 1,746.27 us |   7.04 KB |

> NeoSmart.Caching.Sqlite did not implement `IBufferDistributedCache` in this benchmark and did not provide a configurable disk-usage or maximum-cache-size limit in the reviewed configuration. These are capability differences, not just performance differences.

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

