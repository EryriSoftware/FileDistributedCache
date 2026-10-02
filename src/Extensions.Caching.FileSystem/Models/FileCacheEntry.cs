namespace Eryri.Extensions.Caching.FileSystem.Models;

internal sealed record FileCacheEntry(
    string Key,
    string Path,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastAccessUtc)
{
    public ulong Version { get; init; } = 0;
    public ulong AccessCount { get; init; } = 0;
    public DateTimeOffset? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public DateTimeOffset? Expiration
    {
        get
        {
            DateTimeOffset? sliding = SlidingExpiration.HasValue ? LastAccessUtc.Add(SlidingExpiration.Value) : null;
            return (sliding.HasValue && AbsoluteExpiration.HasValue)
            ? (sliding < AbsoluteExpiration ? sliding : AbsoluteExpiration)
            : (sliding ?? AbsoluteExpiration);
        }
    }
}