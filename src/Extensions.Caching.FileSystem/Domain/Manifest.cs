using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Eryri.Extensions.Caching.FileSystem.Models;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class Manifest(IOptions<FileCacheOptions> options)
{
    private readonly EvictionPolicy policy = options.Value.EvictionPolicy;
    private long _size;
    public long Size => Volatile.Read(ref _size);
    public int Count => files.Count;
    public ICollection<Metadata> Values => files.Values;
    private readonly ConcurrentDictionary<string, Metadata> files = new (StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentPriorityQueue<PriorityCandidate, SequencedValue<long>> lruQueue = new ();
    private readonly ConcurrentPriorityQueue<PriorityCandidate, ulong> lfuQueue = new ();
    private readonly ConcurrentPriorityQueue<PriorityCandidate, long> ttlQueue = new ();
    private readonly ConcurrentQueue<PriorityCandidate> fifoQueue = new ();

    public void Clear()
    {
        Interlocked.Exchange(ref _size, 0);
        files.Clear();
        lruQueue.Clear();
        lfuQueue.Clear();
        ttlQueue.Clear();
        fifoQueue.Clear();
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out Metadata? value) => files.TryGetValue(key, out value);
    public bool TryUpdate(
        string key,
        Metadata value,
        long lastAccessTicks,
        bool isAccessed,
        out Metadata newValue)
    {
        newValue = value with
        {
            LastAccessTicks = lastAccessTicks,
            AccessCount = isAccessed ? value.AccessCount + 1 : value.AccessCount,
            Version = value.Version + 1
        };
        return TryUpdateCore(key: key, newValue, comparisonValue: value);
    }

    public bool TryUpdate(string key, Metadata newValue, Metadata comparisonValue) => TryUpdateCore(
        key: key,
        newValue: newValue with
        {
            Version = comparisonValue.Version + 1
        },
        comparisonValue: comparisonValue);
    private bool TryUpdateCore(string key, Metadata newValue, Metadata comparisonValue)
    {
        if (files.TryUpdate(key, newValue, comparisonValue))
        {
            Enqueue(newValue, comparisonValue);
            Interlocked.Add(ref _size, newValue.SizeBytes - comparisonValue.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(string key, [MaybeNullWhen(false)] out Metadata value)
    {
        if (files.TryRemove(key, out value))
        {
            Interlocked.Add(ref _size, -value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(Metadata value)
    {
        if (files.TryRemove(new KeyValuePair<string, Metadata>(value.Key, value)))
        {
            Interlocked.Add(ref _size, -value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryAdd(Metadata value)
    {
        if (files.TryAdd(value.Key, value))
        {
            Enqueue(value, null);
            Interlocked.Add(ref _size, value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool AddOrReplace(Metadata value, [NotNullWhen(true)] out Metadata? existing)
    {
        while (true)
        {
            if (TryGetValue(value.Key, out existing)
                && TryUpdate(value.Key, value, existing))
            {
                return true;
            }
            else if (TryAdd(value))
            {
                return false;
            }
        }
    }

    private void Enqueue(Metadata value, Metadata? comparisonValue)
    {
        var candidate = new PriorityCandidate(Key: value.Key, Version: value.Version);
        if (value.ExpirationTicks.HasValue)
        {
            ttlQueue.Enqueue(candidate, value.ExpirationTicks.Value);
        }

        switch (policy)
        {
            case EvictionPolicy.TTL:
                break; // TTL queue is always tracked
            case EvictionPolicy.LRU:
                lruQueue.Enqueue(candidate, new SequencedValue<long>(value.LastAccessTicks));
                break;
            case EvictionPolicy.LFU:
                lfuQueue.Enqueue(candidate, value.AccessCount);

                break;
            case EvictionPolicy.FIFO:
                if (comparisonValue == null)
                {
                    fifoQueue.Enqueue(candidate);
                }

                break;
            default:
                throw new NotImplementedException(policy.ToString());
        }
    }

    public void EnqueueTtl(Metadata value)
    {
        if (value.ExpirationTicks.HasValue)
        {
            var candidate = new PriorityCandidate(Key: value.Key, Version: value.Version);
            ttlQueue.Enqueue(candidate, value.ExpirationTicks.Value);
        }
    }

    private bool TryDequeueCandidate(EvictionPolicy policy, out PriorityCandidate candidate) =>
        policy switch
        {
            EvictionPolicy.TTL => ttlQueue.TryDequeue(out candidate),
            EvictionPolicy.LRU => lruQueue.TryDequeue(out candidate),
            EvictionPolicy.LFU => lfuQueue.TryDequeue(out candidate),
            EvictionPolicy.FIFO => fifoQueue.TryDequeue(out candidate),
            _ => throw new NotImplementedException(policy.ToString())
        };

    private bool TryDequeue(EvictionPolicy policy, [NotNullWhen(true)] out Metadata? value)
    {
        while (TryDequeueCandidate(policy, out var candidate))
        {
            if (files.TryGetValue(candidate.Key, out var current) &&
                (policy == EvictionPolicy.FIFO || candidate.Version == current.Version))
            {
                value = current;
                return true;
            }
        }

        value = null;
        return false;
    }

    public bool TryDequeueTTL([NotNullWhen(true)] out Metadata? value) => TryDequeue(EvictionPolicy.TTL, out value);
    public bool TryDequeueEvictionPolicy([NotNullWhen(true)] out Metadata? value) => TryDequeue(policy, out value);
}
