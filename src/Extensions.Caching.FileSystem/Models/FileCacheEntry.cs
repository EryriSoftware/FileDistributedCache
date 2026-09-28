namespace Eryri.Extensions.Caching.FileSystem.Models;

internal sealed record FileCacheEntry(
    string Key,
    string Path,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastAccessUtc)
{
    public int Version { get; init; } = 0;
    public int AccessCount { get; init; } = 0;
    public DateTimeOffset? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public DateTimeOffset? Expiration =>
        AbsoluteExpiration.HasValue
        ? AbsoluteExpiration.Value
        : SlidingExpiration.HasValue
        ? LastAccessUtc.Add(SlidingExpiration.Value)
        : null;
}