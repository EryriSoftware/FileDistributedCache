namespace Eryri.Extensions.Caching.FileSystem;

/// <summary>
/// Specifies options for <see cref="IFileDistributedCache"/>.
/// </summary>
public record FileCacheOptions
{
    private const int BytesPerMiB = 1 << 20;
    private const int BytesPerGiB = 1 << 30;

    /// <summary>
    /// Gets or sets the minimum length of time between removing expired items.
    /// Zero or negative will result in the next execution being scheduled for the next shortest TTL.
    /// </summary>
    public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets the eviction policy used when reducing the number of cached files beyond already expired items.
    /// </summary>
    public EvictionPolicy EvictionPolicy { get; set; } = EvictionPolicy.LRU;

    /// <summary>
    /// Gets or sets the maximum size of the cache in GiB.
    /// </summary>
    public decimal? SizeLimitGiB
    {
        get => SizeLimitBytes / BytesPerGiB;
        set => SizeLimitBytes = Convert.ToInt64(value * BytesPerGiB);
    }

    /// <summary>
    /// Gets or sets the maximum size of the cache in MiB.
    /// </summary>
    public decimal? SizeLimitMiB
    {
        get => SizeLimitBytes / BytesPerMiB;
        set => SizeLimitBytes = Convert.ToInt64(value * BytesPerMiB);
    }

    /// <summary>
    /// Gets or sets the maximum size of the cache in bytes.
    /// </summary>
    public long? SizeLimitBytes
    {
        get => field < 0 ? null : field;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(value)} must be non-negative.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the default sliding expiration for items without one.
    /// </summary>
    public TimeSpan? DefaultSlidingExpiration = null;

    /// <summary>
    /// Gets or sets the default Time To Live for items an absolute expiry set. Calculated from the time the item is added to the cache.
    /// </summary>
    public TimeSpan? DefaultAbsoluteExpirationRelativeToNow = null;

    /// <summary>
    /// Gets or sets the directory where cache files are stored.
    /// Defaults to a tmp directory that's deleted when the application shuts down.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Gets or sets the interval between compacting the Write Ahead Log into a snapshot.
    /// A WAL is used to ensure minimal data loss. The snapshot speeds up recovery time.
    /// </summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);
}
