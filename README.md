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

The benchmark workload currently uses
- 4 KiB payloads
- initial cache filled with 10000 entries
- 100 parallel operations.

### Published results

The following results are the supplied BenchmarkDotNet output for the workload described above. Use these figures as directional evidence and rerun the benchmarks on the target environment before making capacity or latency commitments.

Times are in microseconds (`us`). Allocated memory is per operation. `1 ms = 1,000 us` and `1 us = 1,000 ns`.

#### Eryri.FileDistributedCache - unbounded

| Method                      | Mean        | Error       | StdDev      | Median      | Min        | Allocated |
|---------------------------- |------------:|------------:|------------:|------------:|-----------:|----------:|
| SetNewKey                   |  68.1984 us |   2.0922 us |   5.9352 us |  67.6585 us | 56.7773 us |    1371 B |
| SetNewKeyBuffered           |  75.9309 us |   5.6687 us |  15.5179 us |  70.3292 us | 60.3792 us |    1463 B |
| SetNewKeyAsync              |  61.1024 us |   1.2078 us |   2.8233 us |  61.4290 us | 54.4973 us |    1858 B |
| SetNewKeyBufferedAsync      |  63.9512 us |   1.2703 us |   3.3686 us |  63.5358 us | 54.5604 us |    2183 B |
| SetExistingKey              |  65.0194 us |   1.8970 us |   5.2565 us |  64.8756 us | 51.5435 us |    1317 B |
| SetExistingKeyBuffered      |  72.1364 us |   4.5393 us |  13.2415 us |  68.7404 us | 52.7331 us |    1492 B |
| SetExistingKeyAsync         |  62.2095 us |   1.7267 us |   4.8134 us |  61.5533 us | 50.5821 us |    2168 B |
| SetExistingKeyBufferedAsync |  59.4561 us |   2.1308 us |   5.8689 us |  57.9338 us | 48.9425 us |    2172 B |
| Get                         |  22.2764 us |   0.7138 us |   2.0133 us |  22.0592 us | 16.5960 us |    4928 B |
| GetBuffered                 |  22.7918 us |   0.6331 us |   1.8268 us |  22.7140 us | 17.6354 us |    1065 B |
| GetAsync                    |  22.9685 us |   0.4725 us |   1.3171 us |  23.0533 us | 17.2733 us |    5702 B |
| GetBufferedAsync            |  22.5720 us |   0.5588 us |   1.5852 us |  22.5875 us | 16.8017 us |    1664 B |
| GetMiss                     |   0.2319 us |   0.0230 us |   0.0648 us |   0.2202 us |  0.1375 us |      28 B |
| GetBufferedMiss             |   0.2717 us |   0.0111 us |   0.0311 us |   0.2696 us |  0.1929 us |      62 B |
| GetMissAsync                |   0.5953 us |   0.0479 us |   0.1390 us |   0.5917 us |  0.3013 us |      26 B |
| GetBufferedMissAsync        |   0.6678 us |   0.0521 us |   0.1496 us |   0.6783 us |  0.3767 us |      58 B |
| SetGetExistingKeyAsync      | 108.5485 us |   5.4031 us |  13.7525 us | 108.9281 us | 64.9013 us |    7235 B |

#### Eryri.FileDistributedCache - capacity constrained

This run measures Eryri with capacity enforcement and eviction enabled. It is not directly equivalent to the unbounded run because the cache performs additional capacity work.

| Method                      | Mean        | Error       | StdDev      | Median      | Min        | Allocated |
|---------------------------- |------------:|------------:|------------:|------------:|-----------:|----------:|
| SetNewKey                   |  75.5887 us |   2.6702 us |   7.7042 us |  74.7296 us | 59.6169 us |    1478 B |
| SetNewKeyBuffered           |  72.2988 us |   2.4905 us |   7.1458 us |  70.7137 us | 54.1379 us |    1720 B |
| SetNewKeyAsync              |  73.1605 us |   2.4423 us |   7.0855 us |  71.5567 us | 59.4054 us |    1994 B |
| SetNewKeyBufferedAsync      |  69.5835 us |   2.0364 us |   5.8755 us |  68.7827 us | 54.7060 us |    2645 B |

#### [StackExchange.Redis](https://www.nuget.org/packages/StackExchange.Redis)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean     | Error    | StdDev   | Median   | Min      | Allocated |
|---------------------------- |---------:|---------:|---------:|---------:|---------:|----------:|
| SetNewKey                   | 20.94 us | 0.654 us | 1.677 us | 21.20 us | 16.48 us |    1803 B |
| SetNewKeyAsync              | 28.81 us | 0.573 us | 1.569 us | 28.88 us | 24.80 us |    1620 B |
| SetExistingKey              | 22.54 us | 0.697 us | 2.000 us | 22.58 us | 15.88 us |    1722 B |
| SetExistingKeyAsync         | 27.12 us | 0.536 us | 1.232 us | 27.04 us | 23.99 us |    1522 B |
| Get                         | 19.42 us | 0.536 us | 1.493 us | 19.36 us | 14.60 us |    4689 B |
| GetAsync                    | 25.14 us | 0.627 us | 1.819 us | 24.83 us | 21.83 us |    5145 B |
| GetMiss                     | 18.62 us | 0.497 us | 1.359 us | 18.59 us | 15.01 us |     493 B |
| GetMissAsync                | 22.14 us | 0.579 us | 1.690 us | 22.25 us | 17.14 us |     939 B |
| SetGetExistingKeyAsync      | 30.30 us | 0.555 us | 1.481 us | 30.18 us | 26.15 us |    6634 B |

> StackExchange.Redis did not implement `IBufferDistributedCache` in this benchmark. These are capability differences, not just performance differences.

#### [DamianH.FileDistributedCache](https://www.nuget.org/packages/DamianH.FileDistributedCache)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean       | Error      | StdDev     | Median     | Min        | Allocated |
|---------------------------- |-----------:|-----------:|-----------:|-----------:|-----------:|----------:|
| SetNewKey                   | 183.461 us | 11.8540 us | 34.7656 us | 179.612 us | 129.659 us |   6.75 KB |
| SetNewKeyBuffered           | 181.279 us | 14.7776 us | 42.8725 us | 179.094 us | 100.266 us |   6.78 KB |
| SetNewKeyAsync              | 165.349 us | 15.0484 us | 43.8968 us | 156.314 us |  92.677 us |   6.64 KB |
| SetNewKeyBufferedAsync      | 161.269 us | 12.3849 us | 35.9309 us | 158.660 us |  89.234 us |   6.64 KB |
| SetExistingKey              | 405.241 us |  7.0975 us |  7.2886 us | 408.073 us | 383.613 us |   6.58 KB |
| SetExistingKeyBuffered      | 410.677 us |  7.6830 us |  8.2208 us | 410.134 us | 399.028 us |   6.74 KB |
| SetExistingKeyAsync         | 412.214 us |  7.3541 us |  6.8790 us | 410.487 us | 398.936 us |    6.8 KB |
| SetExistingKeyBufferedAsync | 419.639 us |  8.3233 us |  8.5475 us | 419.328 us | 407.446 us |    6.8 KB |
| Get                         |  44.415 us |  0.8623 us |  0.8066 us |  44.132 us |  43.506 us |   9.03 KB |
| GetBuffered                 |  44.093 us |  0.8694 us |  0.9663 us |  43.916 us |  41.513 us |   5.04 KB |
| GetAsync                    | 131.182 us | 19.0957 us | 56.3042 us | 147.255 us |  42.603 us |   9.75 KB |
| GetBufferedAsync            | 128.434 us | 16.3112 us | 48.0939 us | 137.557 us |  43.552 us |   5.79 KB |
| GetMiss                     |   7.269 us |  0.5684 us |  1.6124 us |   6.845 us |   5.014 us |   2.16 KB |
| GetBufferedMiss             |   8.199 us |  0.4465 us |  1.3094 us |   8.088 us |   5.713 us |   2.19 KB |
| GetMissAsync                |   6.576 us |  0.3684 us |  1.0747 us |   6.483 us |   4.501 us |   2.13 KB |
| GetBufferedMissAsync        |   7.367 us |  0.3803 us |  1.1093 us |   7.339 us |   5.183 us |   2.16 KB |
| SetGetExistingKeyAsync      | 768.271 us | 22.0366 us | 63.2271 us | 763.368 us | 623.854 us |  16.52 KB |

> The comparison implementations do not necessarily provide the same feature set. In particular, their capacity behavior and eviction semantics differ; compare the capabilities you need, not only the mean latency.

#### [NeoSmart.Caching.Sqlite](https://www.nuget.org/packages/NeoSmart.Caching.Sqlite)

This is a comparison run from the same benchmark harness. It is included for context, not as a universal ranking across all workloads.

| Method                      | Mean        | Error      | StdDev     | Median      | Min         | Allocated |
|---------------------------- |------------:|-----------:|-----------:|------------:|------------:|----------:|
| SetNewKey                   |   244.10 us |  39.957 us | 117.186 us |   259.01 us |    85.80 us |   1.46 KB |
| SetNewKeyAsync              |   228.01 us |  34.358 us | 101.304 us |   258.07 us |    87.35 us |   1.36 KB |
| SetExistingKey              |    94.87 us |   2.239 us |   6.530 us |    93.96 us |    83.93 us |   1.41 KB |
| SetExistingKeyAsync         |    80.67 us |   1.922 us |   5.576 us |    80.40 us |    69.77 us |   1.27 KB |
| Get                         | 2,089.93 us | 311.994 us | 919.921 us | 1,578.79 us |    54.17 us |   5.56 KB |
| GetAsync                    | 1,545.61 us |  16.441 us |  14.575 us | 1,542.59 us | 1,528.75 us |   5.78 KB |
| GetMiss                     |    51.81 us |   0.933 us |   1.634 us |    51.40 us |    49.46 us |   1.49 KB |
| GetMissAsync                |    58.13 us |   1.108 us |   1.138 us |    58.00 us |    56.66 us |   1.48 KB |
| SetGetExistingKeyAsync      | 1,502.22 us | 126.527 us | 360.989 us | 1,597.09 us |   116.59 us |   7.04 KB |

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

