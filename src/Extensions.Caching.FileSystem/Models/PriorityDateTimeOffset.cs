namespace Eryri.Extensions.Caching.FileSystem.Models;

internal record struct PriorityDateTimeOffset(long Value) : IComparable<PriorityDateTimeOffset>
{
    private static ulong CurrentSequence = 0;
    public ulong Sequence { get; } = Interlocked.Increment(ref CurrentSequence);
    public int CompareTo(PriorityDateTimeOffset other)
    {
        var timeComparison = Value.CompareTo(other.Value);

        return timeComparison != 0
            ? timeComparison
            : Sequence.CompareTo(other.Sequence);
    }
}
