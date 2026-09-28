# Eryri.FileDistributedCache
##### `using Eryri.Extensions.Caching.FileSystem;`


A filesystem-backed cache for .NET applications, exposed through `IDistributedCache` and `IBufferedDistributedCache`.

Built for low-latency, high-throughput access within a single process, with thread-safe operations and Native AOT compatibility. It supports configurable eviction, entry expiration, and a cache size limit.

> [!IMPORTANT]
> **Process-local, not distributed.** Implementing `IDistributedCache` does not make this a shared cache. Each application process has its own cache; do not use it when multiple instances must see the same entries.

<!-- IMAGE PLACEHOLDER: Add a small, measured benchmark chart or architecture image after verifying the behavior. -->

## Why use it?

- **Familiar integration:** Use the standard `IDistributedCache` API in an application that needs a local cache.
- **Filesystem-backed payloads:** Cache data is stored as files in a temporary directory rather than presented as a shared cache service.
- **Control over capacity:** Set a size limit and choose which entries are evicted when space is needed.
- **Expiration built in:** Configure absolute or sliding expiration per entry; expired entries are removed automatically.
- **Deployment flexibility:** Synchronous and asynchronous operations, thread safety, `IBufferedDistributedCache`, and Native AOT compatibility.

A good fit is a single-process service or worker whose cached values are disposable and can be regenerated. If you need a shared cache across replicas or persistence across restarts, choose a backend designed for that requirement.

## Install

```bash
dotnet add package Eryri.FileDistributedCache
```

> [!NOTE]
> The NuGet package ID is `Eryri.FileDistributedCache`; the namespace is `Eryri.Extensions.Caching.FileSystem`.

## Quick start

Register the cache and use it through `IDistributedCache`:

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

In a real application, fetch or compute the value on a miss and write it back to the cache. Always treat a cache miss as normal, including after expiration or restart.

<!-- EXAMPLE PLACEHOLDER: Link to a complete ASP.NET Core or worker-service example in this repository. -->

### Use with HybridCache

Register this package as the secondary cache provider, then register `HybridCache`:

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
    LocalCacheExpiration = TimeSpan.FromSeconds(5),
    Flags = HybridCacheEntryFlags.DisableLocalCache // Disables the 'IMemoryCache' for the purpose of thie example
};

await cache.SetAsync("product:42", "Cached product data", options);

var value = await cache.GetOrCreateAsync(
    "product:42",
    _ => ValueTask.FromResult("Value to create on a miss"),
    options);

Console.WriteLine(value);
```

`DisableLocalCache` makes the `HybridCache` use the secondary `IDistributedCache` cache rather than a `IMemoryCache` local hit. Remove that flag when you want the normal `HybridCache` local in-memory tier. The filesystem cache remains process-local in either case.

<!-- DIAGRAM PLACEHOLDER: Once verified, show Application -> HybridCache local tier -> filesystem-backed secondary provider; label both tiers process-local. -->

## Expiration and eviction

Choose an eviction policy to determine which entries are removed when an incoming write would exceed the configured cache size limit:

| Policy | Selection principle |
| --- | --- |
| LRU | Least recently used. |
| LFU | Least frequently used. |
| TTL | Soonest to expire. |
| FIFO | First added. |

Entries can use `DistributedCacheEntryOptions.AbsoluteExpiration`, `AbsoluteExpirationRelativeToNow`, and `SlidingExpiration`. A background timer removes expired entries; it is scheduled for the next entry due to expire.
If a newly added entry would exceed the configured size limit, expired entries are removed according to the selected eviction policy.

When no size limit is configured, `IFileDistributedCache.Compact(double percentage)` can manually remove at least the specified fraction of entries according to the eviction policy. For example, `0.10` requests removal of at least 10%.

<!-- DIAGRAM PLACEHOLDER: Illustrate expiration, size-limit check, and policy-based eviction; verify the exact write path before publishing. -->

## Configuration

| Option | Purpose |
| --- | --- |
| `ExpirationScanFrequency` | Minimum interval between expired-entry cleanup runs. A zero or negative value schedules the next run for the soonest upcoming expiration. |
| `EvictionPolicy` | Policy used when size-limit eviction or manual compaction needs to select entries. |
| `SizeLimitGiB` | Cache size limit expressed in GiB. |
| `SizeLimitMiB` | Cache size limit expressed in MiB. |
| `SizeLimitBytes` | Cache size limit expressed in bytes. |
| `DefaultSlidingExpiration` | Default sliding expiration for items without one. |
| `DefaultAbsoluteExpirationRelativeToNow` | Default Time To Live for items an absolute expiry set. Calculated from the time the item is added to the cache. |

<!-- INSTRUCTIONS PLACEHOLDER: Insert a verified, compilable AddDistributedFileCache(options => ...) example, including the actual option type, property names, and defaults. Explain how multiple size-limit properties interact if more than one is set. -->

## Operational behavior

- **Storage:** Cache files live in a temporary directory under the current user's temp folder. Normal application shutdown removes them.
- **Durability:** Treat entries as disposable. Applications must handle misses and be able to recreate their values; this is not persistent storage.
- **Permissions:** The process needs permission to create, read, write, and delete files in its temporary directory.
- **Abrupt termination:** Files may remain after a crash or forced shutdown. Do not assume cleanup always runs.
- **Write failures:** If the filesystem rejects a write, the entry is not added and an error is logged. Callers should not treat `Set` as proof that the entry can later be read back without checking the actual failure behavior.
- **Multiple processes:** Do not assume the same cache directory or cache state can be shared safely across processes. Each process should be treated as having an independent cache.

<!-- INSTRUCTIONS PLACEHOLDER: Document the exact temp-directory naming scheme and a safe stale-file cleanup procedure after inspecting the implementation. -->
<!-- INSTRUCTIONS PLACEHOLDER: Document any exception/logging behavior and the response to a full or read-only disk based on tests. -->

## What this is not

The interface name can be misleading: `IDistributedCache` describes the API, not a guarantee that every implementation shares state between processes. This package is intended as a local cache. It should not be marketed as Redis-compatible in deployment semantics, a durable database, or an automatic RAM-to-disk overspill engine unless the implementation and measurements substantiate those claims.

## Performance and benchmarks

The useful question is not just “How fast is a hit?” It is “How does the cache behave when it expires entries, fills its size budget, and serves concurrent requests?” Publish results with benchmark code, machine specifications, .NET version, filesystem, storage medium, and cache settings so readers can reproduce them.

#### Benchmark: Read/Write
Add an item and immediately read it.

- How many items? 1000

| Payload size| Type | Percentile| Time (ms)|
| --- | --- | --- | --- |
| 1 byte | write | P01 | 0.372 ms |
|        |       | P10 | 0.406 ms |
|        |       | P50 | 0.449 ms |
|        |       | P95 | 0.612 ms |
|        |       | P99 | 0.803 ms |
| 1 byte | read | P01 | 0.065 ms |
|        |      | P10 | 0.071 ms |
|        |      | P50 | 0.086 ms |
|        |      | P95 | 0.129 ms |
|        |      | P99 | 0.190 ms |
| 1 KiB | write | P01 | 0.371 ms |
|       |       | P10 | 0.390 ms |
|       |       | P50 | 0.437 ms |
|       |       | P95 | 0.650 ms |
|       |       | P99 | 1.958 ms |
| 1 KiB | read | P01 | 0.701 ms |
|       |      | P10 | 0.738 ms |
|       |      | P50 | 0.808 ms |
|       |      | P95 | 1.016 ms |
|       |      | P99 | 1.137 ms |

#### Benchmark: Full-cache churn
Set a fixed size limit; keep adding entries beyond its limit so that items are auto evicted to make room for new ones;

- How many items added after the cache is full? 1000
- Payload size? 1 byte

| EvictionPolicy | Percentile| Time (ms)|
| --- | --- | --- |
| FIFO | P01 | 0.372 ms |
|      | P10 | 0.406 ms |
|      | P50 | 0.449 ms |
|      | P95 | 0.612 ms |
|      | P99 | 0.803 ms |
| LFU | P01 | 0.361 ms |
|     | P10 | 0.396 ms |
|     | P50 | 0.441 ms |
|     | P95 | 0.616 ms |
|     | P99 | 0.960 ms |
| LRU | P01 | 0.386 ms |
|     | P10 | 0.417 ms |
|     | P50 | 0.451 ms |
|     | P95 | 0.651 ms |
|     | P99 | 0.949 ms |
| TTL | P01 | 0.363 ms |
|     | P10 | 0.401 ms |
|     | P50 | 0.443 ms |
|     | P95 | 0.638 ms |
|     | P99 | 0.928 ms |

