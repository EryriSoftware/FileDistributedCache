using System.Diagnostics.CodeAnalysis;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal sealed class ConcurrentPriorityQueue<TElement, TPriority>
    where TElement : notnull
{
    private readonly record struct Entry(
        TElement Key,
        TPriority Value,
        ulong Sequence);

    private readonly Dictionary<TElement, Entry> _byKey;
    private readonly SortedSet<Entry> _byValue;
    private readonly object _gate = new();

    private ulong _nextSequence;

    public ConcurrentPriorityQueue(
        IComparer<TPriority>? valueComparer = null,
        IEqualityComparer<TElement>? keyComparer = null)
    {
        _byKey = new Dictionary<TElement, Entry>(keyComparer);

        valueComparer ??= Comparer<TPriority>.Default;

        _byValue = new SortedSet<Entry>(
            Comparer<Entry>.Create((a, b) =>
            {
                int result = valueComparer.Compare(a.Value, b.Value);

                return result != 0
                    ? result
                    : a.Sequence.CompareTo(b.Sequence);
            }));
    }

    internal void Enqueue(TElement key, TPriority value)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var existing))
            {
                _byValue.Remove(existing);
            }

            var entry = new Entry(key, value, Interlocked.Increment(ref _nextSequence));

            _byKey[key] = entry;
            _byValue.Add(entry);
        }
    }

    internal bool Remove(TElement key)
    {
        lock (_gate)
        {
            return _byKey.Remove(key, out var entry) && _byValue.Remove(entry);
        }
    }

    internal bool TryDequeue(
        [MaybeNullWhen(false)] out TElement key,
        [MaybeNullWhen(false)] out TPriority value)
    {
        lock (_gate)
        {
            if (_byValue.Count == 0)
            {
                key = default;
                value = default;
                return false;
            }

            var entry = _byValue.Min!;

            _byValue.Remove(entry);
            _byKey.Remove(entry.Key, out _);

            key = entry.Key;
            value = entry.Value;
            return true;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _byKey.Clear();
            _byValue.Clear();
        }
    }
}
