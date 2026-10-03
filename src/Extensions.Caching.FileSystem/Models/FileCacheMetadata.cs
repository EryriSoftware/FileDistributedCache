namespace Eryri.Extensions.Caching.FileSystem.Models;

internal sealed record FileCacheMetadata(
    string Key,
    string Path,
    long SizeBytes,
    long CreatedTicks,
    long LastAccessTicks)
{
    public ulong Version { get; init; } = 0;
    public ulong AccessCount { get; init; } = 0;
    public long? AbsoluteExpirationTicks { get; init; }
    public long? SlidingExpirationTicks { get; init; }
    public long? ExpirationTicks
    {
        get
        {
            long? sliding = SlidingExpirationTicks.HasValue ? LastAccessTicks + SlidingExpirationTicks.Value : null;
            return (sliding.HasValue && AbsoluteExpirationTicks.HasValue)
            ? (sliding < AbsoluteExpirationTicks ? sliding : AbsoluteExpirationTicks)
            : (sliding ?? AbsoluteExpirationTicks);
        }
    }
}