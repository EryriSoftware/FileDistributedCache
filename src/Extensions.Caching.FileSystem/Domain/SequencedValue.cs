namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal record struct SequencedValue<T>(T Value) : IComparable<SequencedValue<T>>
{
    private static ulong CurrentSequence = 0;
    public ulong Sequence { get; } = Interlocked.Increment(ref CurrentSequence);
    private static IComparer<T> Comparer = Comparer<T>.Default;
    public int CompareTo(SequencedValue<T> other)
    {
        var timeComparison = Comparer.Compare(Value, other.Value);

        return timeComparison != 0
            ? timeComparison
            : Sequence.CompareTo(other.Sequence);
    }
}
