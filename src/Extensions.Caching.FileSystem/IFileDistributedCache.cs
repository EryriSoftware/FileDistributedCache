using System.ComponentModel.DataAnnotations;
using Eryri.Extensions.Caching.FileSystem;
using Microsoft.Extensions.Caching.Distributed;

namespace Eryri.Extensions.Caching.FileSystem;

/// <summary>
/// Represents a local file system cache whose values are:
/// <list type="bullet">
///   <item>Unique to this runtime</item>
///   <item>Cleared on appliactin shutdown</item>
/// </list>
/// </summary>
public interface IFileDistributedCache : IDistributedCache, IBufferDistributedCache
{
    /// <summary>
    /// Gets the size of the cache in bytes.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Remove at least the given percentage (0.10 for 10%) of the total entries, according to the the <see cref="FileCacheOptions.EvictionPolicy"/>.
    /// </summary>
    public void Compact([Range(0, 1)] decimal percentage);
}
