# Eryri.Extensions.Caching.FileSystem

A filesystem-backed cache for .NET applications using `IDistributedCache` / `IBufferedDistributedCache`.

> Designed for low-latency, high-throughput access within a single process,
> with thread-safe operations and Native AOT compatibility.

> [!IMPORTANT]
> Despite implementing IDistributedCache, this is local to one process, not a shared backend.


## Features

- Stores cache payloads on the filesystem.
- Eviction policies include:
  - Least Recently Used (LRU)
  - Least Frequently Used (LFU)
  - Soonest Time To Live (TTL)
  - First In First Out (FIFO).
- Supports `DistributedCacheEntryOptions` options for each entry: AbsoluteExpiration, AbsoluteExpirationRelativeToNow, SlidingExpiration.
- Supports synchronous and asynchronous cache operations.
- Auto eviction of expired items.
- Auto eviction when new items would cause the cache to exceed its size limit.

## Installation

```bash
dotnet add package Eryri.FileDistributedCache
```

## Quick start

```csharp
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
    .AddDistributedFileCache()
    .BuildServiceProvider();

var cache = services.GetRequiredService<IDistributedCache>();
var key = Guid.NewGuid().ToString("N");
var payload = Guid.NewGuid().ToString("N");

await cache.SetStringAsync(key, payload);
var actual = await cache.GetStringAsync(key);
```

## Configuration

| Option | Purpose |
| --- | --- |
| [ExpirationScanFrequency] | Minimum length of time between removing expired items. Zero or negative will result in the next execution being scheduled for the next shortest TTL. |
| [EvictionPolicy] | Eviction policy used when reducing the number of cached files beyond already expired items.. |
| [SizeLimitGiB] | Maximum size of the cache in GiB. |
| [SizeLimitMiB] | Maximum size of the cache in MiB. |
| [SizeLimitBytes] | Maximum size of the cache in bytes. |

## Expiration and eviction

- Expiration: A background timer removes expired entries. It is scheduled for the next entry due to expire.
- Size limits: When an added entry would exceed the configured limit, the cache removes entries according to the eviction policy to make room.
- Manual compaction: When no size limit is configured, call `IFileDistributedCache.Compact(double percentage)` to remove at least the specified fraction of entries. For example, 0.10 removes at least 10%, selected according to the eviction policy.
- Write failures: If the filesystem rejects a write, the entry is not added to the cache.
- Storage location: Cache files are stored in a temporary directory under the current user’s temp folder and removed during normal application shutdown.

## Operational notes

- **Durability:** Treat cached entries as disposable. The application must be
  able to recreate data after a restart or cache miss.
- **Permissions:** The application identity needs permission to create, read,
  write, and delete files in its temporary directory.
- **Cleanup:** Cache files are removed during normal shutdown. An abrupt exit
  may leave files behind; [document how these are found or cleaned up].
- **Filesystem failures:** Failed file creation, returns without
  caching, and logs an error with the underlying filesystem error.]