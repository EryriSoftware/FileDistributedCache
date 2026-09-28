namespace Eryri.Extensions.Caching.FileSystem;

/// <summary>
/// The different eviction policies used when reducing the number of cached files beyond already expired items.
/// </summary>
public enum EvictionPolicy
{
    /// <summary>
    /// Least Recently Used.
    /// </summary>
    LRU,

    /// <summary>
    /// Least Frequently Used.
    /// </summary>
    LFU,

    /// <summary>
    /// earliest Time To Live.
    /// </summary>
    TTL,

    /// <summary>
    /// First In First Out.
    /// </summary>
    FIFO,
}
