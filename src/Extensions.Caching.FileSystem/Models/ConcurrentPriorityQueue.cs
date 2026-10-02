using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Eryri.Extensions.Caching.FileSystem.Models;

internal class ConcurrentPriorityQueue<TElement, TPriority>
{
    private readonly object sync = new ();
    private readonly ConcurrentQueue<(TElement Element, TPriority Priority)> pending = new ();
    private PriorityQueue<TElement, TPriority> queue = new PriorityQueue<TElement, TPriority>();

    public void Enqueue(TElement element, TPriority priority)
    {
        pending.Enqueue((element, priority));
    }

    public bool TryDequeue([MaybeNullWhen(false)] out TElement element)
    {
        lock (sync)
        {
            while (pending.TryDequeue(out var item))
            {
                queue.Enqueue(item.Element, item.Priority);
            }

            return queue.TryDequeue(out element, out _);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            pending.Clear();
            queue.Clear();
        }
    }
}
