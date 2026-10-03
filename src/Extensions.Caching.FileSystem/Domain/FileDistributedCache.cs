using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Eryri.Extensions.Caching.FileSystem.Extensions;
using Eryri.Extensions.Caching.FileSystem.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class FileDistributedCache : IFileDistributedCache, IDisposable
{
    public FileDistributedCache(
        TimeProvider timeProvider,
        FileCacheManifest manifest,
        CacheDirectoryOwner directoryOwner,
        IOptions<FileCacheOptions> optionsAccessor,
        ILogger<FileDistributedCache>? logger = null)
    {
        this.timeProvider = timeProvider;
        settings = optionsAccessor.Value;
        minScanFrequencyTicks = settings.ExpirationScanFrequency.Ticks;
        this.logger = logger;
        cleanupTimer = timeProvider.CreateTimer(_ => RemoveExpired(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        this.manifest = manifest;
        cacheDirectory = directoryOwner.Directory;
    }

    private readonly ConcurrentDictionary<string, Lazy<DirectoryInfo>> directories = new (StringComparer.OrdinalIgnoreCase);
    private bool isDisposed = false;
    private readonly ILogger? logger;
    private readonly FileCacheOptions settings;
    private readonly long minScanFrequencyTicks;
    private readonly TimeProvider timeProvider;
    private readonly DirectoryInfo cacheDirectory;
    private readonly FileCacheManifest manifest;
    private long nextCleanup = DateTimeOffset.MaxValue.UtcTicks;
    private readonly ITimer cleanupTimer;

    public long Size => manifest.Size;

    public bool TryGet(string key, IBufferWriter<byte> destination)
    {
        if (manifest.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
        {
            UpdateEntry(key, true);
            try
            {
                destination.ReadFile(entry.Path);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "Failed to read cache file");
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    public byte[]? Get(string key)
    {
        if (manifest.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
        {
            UpdateEntry(key, true);
            try
            {
                return File.ReadAllBytes(entry.Path);
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "Failed to read cache file");
                return null;
            }
        }
        else
        {
            return null;
        }
    }

    public async ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken cancellationToken)
    {
        if (manifest.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
        {
            UpdateEntry(key, true);
            try
            {
                await destination.ReadFileAsync(entry.Path, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "Failed to read cache file");
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (manifest.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
        {
            UpdateEntry(key, true);
            try
            {
                return await File.ReadAllBytesAsync(entry.Path, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "Failed to read cache file");
                return null;
            }
        }
        else
        {
            return null;
        }
    }

    public void Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
    {
        var entry = CreateFileEntry(key, value.Length, options);
        EnsureCapacityFor(entry.SizeBytes);

        try
        {
            value.WriteAllBytes(entry.Path);
            PublishEntry(entry);
        }
        catch (Exception ex)
        {
            TryDelete(entry.Path);
            logger?.LogCritical(ex, "Failed to write cache file");
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var entry = CreateFileEntry(key, value.Length, options);
        EnsureCapacityFor(entry.SizeBytes);

        try
        {
            File.WriteAllBytes(entry.Path, value);
            PublishEntry(entry);
        }
        catch (Exception ex)
        {
            TryDelete(entry.Path);
            logger?.LogCritical(ex, "Failed to write cache file");
        }
    }

    public async ValueTask SetAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options, CancellationToken cancellationToken)
    {
        var entry = CreateFileEntry(key, value.Length, options);
        EnsureCapacityFor(entry.SizeBytes);

        try
        {
            await value.WriteAllBytesAsync(entry.Path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PublishEntry(entry);
        }
        catch (Exception ex)
        {
            TryDelete(entry.Path);
            logger?.LogCritical(ex, "Failed to write cache file");
        }
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken cancellationToken)
    {
        var entry = CreateFileEntry(key, value.Length, options);
        EnsureCapacityFor(entry.SizeBytes);

        try
        {
            await File.WriteAllBytesAsync(entry.Path, value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PublishEntry(entry);
        }
        catch (Exception ex)
        {
            TryDelete(entry.Path);
            logger?.LogCritical(ex, "Failed to write cache file");
        }
    }

    public void Refresh(string key)
    {
        UpdateEntry(key, false);
    }

    private void UpdateEntry(string key, bool isAccessed)
    {
        while (manifest.TryGetValue(key, out var entry))
        {
            var newEntry = entry with
            {
                LastAccessTicks = timeProvider.GetUtcNow().UtcTicks,
                AccessCount = isAccessed ? entry.AccessCount + 1 : entry.AccessCount
            };

            if (manifest.TryUpdate(key, newEntry, entry))
            {
                QueueCleanup(newEntry.ExpirationTicks);
                return;
            }
        }
    }

    private bool RemoveIfExpired(FileCacheMetadata entry) =>
        entry.ExpirationTicks is { } expiry && expiry < timeProvider.GetUtcNow().UtcTicks && Remove(entry);

    public Task RefreshAsync(string key, CancellationToken cancellationToken)
    {
        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        if (manifest.TryRemove(key, out var entry))
        {
            TryDelete(entry.Path);
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    private bool Remove(FileCacheMetadata entry)
    {
        if (manifest.TryRemove(new KeyValuePair<string, FileCacheMetadata>(entry.Key, entry)))
        {
            TryDelete(entry.Path);
            return true;
        }

        return false;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger?.LogCritical(ex, "Failed to delete cache file");
        }
    }

    private int ShardingDepth()
    {
        const long itemsPerFolder = 1 << 10; // ( * 1024 )
        var count = manifest.Count;

        for (var depth = 1; depth < 10; depth++)
        {
            if (count < itemsPerFolder * (1L << (depth * 4)))
            {
                return depth;
            }
        }

        return 10;
    }

    private FileCacheMetadata CreateFileEntry(string key, long payloadSize, DistributedCacheEntryOptions options)
    {
        var id = Guid.NewGuid().ToString("N");
        var shard = id[..ShardingDepth()];
        var now = timeProvider.GetUtcNow().UtcTicks;
        var path = Path.Combine(cacheDirectory.FullName, shard, $"{id}.bytes");
        var directory = directories
            .GetOrAdd(shard, static (shard, root) => new Lazy<DirectoryInfo>(() => Directory.CreateDirectory(Path.Combine(root.FullName, shard))), cacheDirectory)
            .Value; // Ensure the directory is created before writing the file
        return new FileCacheMetadata(
            Key: key,
            Path: path,
            SizeBytes: payloadSize,
            CreatedTicks: now)
        {
            LastAccessTicks = now,
            AbsoluteExpirationTicks = options.AbsoluteExpiration.HasValue
                ? options.AbsoluteExpiration.Value.UtcTicks
                : options.AbsoluteExpirationRelativeToNow.HasValue
                ? now + options.AbsoluteExpirationRelativeToNow.Value.Ticks
                : settings.DefaultAbsoluteExpirationRelativeToNow.HasValue
                ? now + settings.DefaultAbsoluteExpirationRelativeToNow.Value.Ticks
                : null,
            SlidingExpirationTicks = options.SlidingExpiration?.Ticks ?? settings.DefaultSlidingExpiration?.Ticks
        };
    }

    private void PublishEntry(FileCacheMetadata entry)
    {
        while (true)
        {
            if (manifest.TryGetValue(entry.Key, out var existing)
                && manifest.TryUpdate(entry.Key, entry, existing))
            {
                TryDelete(existing.Path);
                QueueCleanup(entry.ExpirationTicks);
                return;
            }
            else if (manifest.TryAdd(entry.Key, entry))
            {
                QueueCleanup(entry.ExpirationTicks);
                return;
            }
        }
    }

    private void QueueCleanup(long? dueTimeTicks)
    {
        if (dueTimeTicks is { } dueTicks && dueTicks <= nextCleanup)
        {
            nextCleanup = dueTicks;
            var seconds = Math.Max(minScanFrequencyTicks, dueTicks - timeProvider.GetUtcNow().UtcTicks);
            cleanupTimer.Change(TimeSpan.FromTicks(seconds), Timeout.InfiniteTimeSpan);
        }
    }

    public void Compact([Range(0, 1)] decimal percentage = 0)
    {
        var requiredFreeSpace = Convert.ToInt64(manifest.Size * percentage);

        while (requiredFreeSpace > 0 && manifest.TryDequeueEvictionPolicy(out var item))
        {
            Remove(item);
            requiredFreeSpace -= item.SizeBytes;
        }
    }

    private long RemoveExpired()
    {
        nextCleanup = DateTimeOffset.MaxValue.UtcTicks;
        long removedBytes = 0;
        var now = timeProvider.GetUtcNow().UtcTicks;
        while (manifest.TryDequeueTTL(out var item))
        {
            if (item.ExpirationTicks <= now)
            {
                Remove(item);
                removedBytes += item.SizeBytes;
            }
            else
            {
                manifest.EnqueueTtl(item);
                QueueCleanup(item.ExpirationTicks);
                break;
            }
        }

        return removedBytes;
    }

    private void EnsureCapacityFor(long requiredFreeSpace)
    {
        if (settings.SizeLimitBytes > 0 && requiredFreeSpace < settings.SizeLimitBytes)
        {
            while (requiredFreeSpace + manifest.Size > settings.SizeLimitBytes
                && manifest.TryDequeueEvictionPolicy(out var item))
            {
                Remove(item);
            }
        }
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            cleanupTimer.Dispose();
        }
    }
}
