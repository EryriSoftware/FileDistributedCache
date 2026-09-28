using System;
using System.Collections.Generic;
using System.Text;

namespace Eryri.Extensions.Caching.FileSystem.Models;

internal record struct PriorityDateTimeOffset(DateTimeOffset Value) : IComparable<PriorityDateTimeOffset>
{
    private static long CurrentSequence = 0;
    public long Sequence { get; } = Interlocked.Increment(ref CurrentSequence);
    public int CompareTo(PriorityDateTimeOffset other)
    {
        var timeComparison = Value.CompareTo(other.Value);

        return timeComparison != 0
            ? timeComparison
            : Sequence.CompareTo(other.Sequence);
    }
}
