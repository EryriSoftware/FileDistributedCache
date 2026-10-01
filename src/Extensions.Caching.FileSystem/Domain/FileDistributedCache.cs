using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
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
        IOptions<FileCacheOptions> optionsAccessor,
        ILogger<FileDistributedCache>? logger = null)
    {
        this.timeProvider = timeProvider;
        settings = optionsAccessor.Value;
        this.logger = logger;
        cleanupTimer = timeProvider.CreateTimer(_ => RemoveExpired(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        metadata = new FileCacheMetadata(optionsAccessor.Value.EvictionPolicy);
    }

    private bool isDisposed = false;
    private readonly ILogger? logger;
    private readonly FileCacheOptions settings;
    private readonly TimeProvider timeProvider;
    private readonly DirectoryInfo cacheDirectory = Directory.CreateTempSubdirectory();
    private readonly FileCacheMetadata metadata;
    private DateTimeOffset nextCleanup = DateTimeOffset.MaxValue;
    private readonly ITimer cleanupTimer;

    public long Size => metadata.Size;

    public bool TryGet(string key, IBufferWriter<byte> destination)
    {
        if (metadata.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
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
        if (metadata.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
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
        if (metadata.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
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
        if (metadata.TryGetValue(key, out var entry) && !RemoveIfExpired(entry))
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
        while (metadata.TryGetValue(key, out var entry))
        {
            var now = timeProvider.GetUtcNow();
            var newEntry = entry with
            {
                LastAccessUtc = now,
                AccessCount = isAccessed ? entry.AccessCount + 1 : entry.AccessCount
            };

            if (metadata.TryUpdate(key, newEntry, entry))
            {
                QueueCleanup(newEntry.Expiration);
                return;
            }
        }
    }

    private bool RemoveIfExpired(FileCacheEntry entry) =>
        entry.Expiration is { } expiry && expiry < timeProvider.GetUtcNow() && Remove(entry);

    public Task RefreshAsync(string key, CancellationToken cancellationToken)
    {
        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        if (metadata.TryRemove(key, out var entry))
        {
            TryDelete(entry.Path);
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    private bool Remove(FileCacheEntry entry)
    {
        if (metadata.TryRemove(new KeyValuePair<string, FileCacheEntry>(entry.Key, entry)))
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

    private FileCacheEntry CreateFileEntry(string key, long payloadSize, DistributedCacheEntryOptions options)
    {
        var now = timeProvider.GetUtcNow();
        return new FileCacheEntry(
            Key: key,
            Path: Path.Combine(cacheDirectory.FullName, $"{Guid.NewGuid():N}.bytes"),
            SizeBytes: payloadSize,
            CreatedUtc: now,
            LastAccessUtc: now)
        {
            AbsoluteExpiration = options.AbsoluteExpirationRelativeToNow.HasValue
                ? now.Add(options.AbsoluteExpirationRelativeToNow.Value)
                : options.AbsoluteExpiration.HasValue
                ? options.AbsoluteExpiration
                : settings.DefaultSlidingExpiration.HasValue
                ? now.Add(settings.DefaultSlidingExpiration.Value)
                : null,
            SlidingExpiration = options.SlidingExpiration ?? settings.DefaultSlidingExpiration
        };
    }

    private void PublishEntry(FileCacheEntry entry)
    {
        while (true)
        {
            if (metadata.TryGetValue(entry.Key, out var existing)
                && metadata.TryUpdate(entry.Key, entry, existing))
            {
                TryDelete(existing.Path);
                QueueCleanup(entry.Expiration);
                return;
            }
            else if (metadata.TryAdd(entry.Key, entry))
            {
                QueueCleanup(entry.Expiration);
                return;
            }
        }
    }

    private void QueueCleanup(DateTimeOffset? dueTime)
    {
        if (dueTime.HasValue && dueTime <= nextCleanup)
        {
            var now = timeProvider.GetUtcNow();
            nextCleanup = dueTime.Value;
            var seconds = Math.Max(settings.ExpirationScanFrequency.TotalSeconds, (dueTime.Value - now).TotalSeconds);
            cleanupTimer.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
        }
    }

    public void Compact([Range(0, 1)] decimal percentage = 0)
    {
        var requiredFreeSpace = Convert.ToInt64(metadata.Size * percentage);

        while (requiredFreeSpace > 0 && metadata.TryDequeueEvictionPolicy(out var item))
        {
            Remove(item);
            requiredFreeSpace -= item.SizeBytes;
        }
    }

    private long RemoveExpired()
    {
        nextCleanup = DateTimeOffset.MaxValue;
        long removedBytes = 0;
        var now = timeProvider.GetUtcNow();
        while (metadata.TryDequeueTTL(out var item))
        {
            if (item.Expiration <= now)
            {
                Remove(item);
                removedBytes += item.SizeBytes;
            }
            else
            {
                metadata.EnqueueTtl(item);
                QueueCleanup(item.Expiration);
                break;
            }
        }

        return removedBytes;
    }

    private void EnsureCapacityFor(long requiredFreeSpace)
    {
        if (settings.SizeLimitBytes > 0 && requiredFreeSpace < settings.SizeLimitBytes)
        {
            while (requiredFreeSpace + metadata.Size > settings.SizeLimitBytes
                && metadata.TryDequeueEvictionPolicy(out var item))
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
            cacheDirectory.Delete(recursive: true);
            cleanupTimer.Dispose();
            metadata.Clear();
        }
    }
}
