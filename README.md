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

#### Eryri.FileDistributedCache - capacity constrained

This run measures Eryri with capacity enforcement and eviction enabled. It is not directly equivalent to the unbounded run because the cache performs additional capacity work.

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

#### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

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

> The comparison implementations do not necessarily provide the same feature set. In particular, their capacity behavior and eviction semantics differ; compare the capabilities you need, not only the mean latency.

#### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)

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

