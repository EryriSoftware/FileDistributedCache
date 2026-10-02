namespace Eryri.Extensions.Caching.FileSystem.Models;

internal record struct SequencedValue<T>(T Value) : IComparable<SequencedValue<T>>
{
    private static ulong CurrentSequence = 0;
    public ulong Sequence { get; } = Interlocked.Increment(ref CurrentSequence);
    public int CompareTo(SequencedValue<T> other)
    {
        var timeComparison = Comparer<T>.Default.Compare(Value, other.Value);

        return timeComparison != 0
            ? timeComparison
            : Sequence.CompareTo(other.Sequence);
    }
}
