namespace Eryri.Extensions.Caching.FileSystem.Models;

internal sealed record FileCacheMetadata(
    string Key,
    string Path,
    long SizeBytes,
    long CreatedTicks)
{
    public ulong Version { get; init; } = 0;
    public ulong AccessCount { get; init; } = 0;
    public long LastAccessTicks
    {
        get => field;
        init
        {
            field = value;
            CalculateExpirationTicks();
        }
    }

    public long? AbsoluteExpirationTicks
    {
        get => field;
        init
        {
            field = value;
            CalculateExpirationTicks();
        }
    }

    public long? SlidingExpirationTicks
    {
        get => field;
        init
        {
            field = value;
            CalculateExpirationTicks();
        }
    }

    public long? ExpirationTicks { get; private set; }

    private void CalculateExpirationTicks()
    {
        long? sliding = SlidingExpirationTicks.HasValue ? LastAccessTicks + SlidingExpirationTicks.Value : null;
        ExpirationTicks = (sliding.HasValue && AbsoluteExpirationTicks.HasValue)
        ? (sliding < AbsoluteExpirationTicks ? sliding : AbsoluteExpirationTicks)
        : (sliding ?? AbsoluteExpirationTicks);
    }
}