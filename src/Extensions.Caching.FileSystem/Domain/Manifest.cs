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
    private readonly ConcurrentDictionary<string, Metadata> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentPriorityQueue<string, long> lruQueue = new();
    private readonly ConcurrentPriorityQueue<string, ulong> lfuQueue = new();
    private readonly ConcurrentPriorityQueue<string, long> ttlQueue = new();
    private readonly ConcurrentQueue<string> fifoQueue = new();

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
            Dequeue(value);
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
        EnqueueTtl(value);

        switch (policy)
        {
            case EvictionPolicy.TTL:
                break; // TTL queue is always tracked
            case EvictionPolicy.LRU:
                lruQueue.Enqueue(value.Key, value.LastAccessTicks);
                break;
            case EvictionPolicy.LFU:
                lfuQueue.Enqueue(value.Key, value.AccessCount);

                break;
            case EvictionPolicy.FIFO:
                if (comparisonValue == null)
                {
                    fifoQueue.Enqueue(value.Key);
                }

                break;
            default:
                throw new NotImplementedException(policy.ToString());
        }
    }

    private void Dequeue(Metadata value)
    {
        ttlQueue.Remove(value.Key);

        switch (policy)
        {
            case EvictionPolicy.TTL:
                break; // TTL queue is always tracked
            case EvictionPolicy.LRU:
                lruQueue.Remove(value.Key);
                break;
            case EvictionPolicy.LFU:
                lfuQueue.Remove(value.Key);
                break;
            case EvictionPolicy.FIFO:
                fifoQueue.Enqueue(value.Key);
                break;
            default:
                throw new NotImplementedException(policy.ToString());
        }
    }

    public void EnqueueTtl(Metadata value)
    {
        if (value.ExpirationTicks.HasValue)
        {
            ttlQueue.Enqueue(value.Key, value.ExpirationTicks.Value);
        }
    }

    private bool TryDequeueCandidate(EvictionPolicy policy, [MaybeNullWhen(false)] out string candidate) =>
        policy switch
        {
            EvictionPolicy.TTL => ttlQueue.TryDequeue(out candidate, out _),
            EvictionPolicy.LRU => lruQueue.TryDequeue(out candidate, out _),
            EvictionPolicy.LFU => lfuQueue.TryDequeue(out candidate, out _),
            EvictionPolicy.FIFO => fifoQueue.TryDequeue(out candidate),
            _ => throw new NotImplementedException(policy.ToString())
        };

    private bool TryDequeue(EvictionPolicy policy, [NotNullWhen(true)] out Metadata? value)
    {
        while (TryDequeueCandidate(policy, out var candidate))
        {
            if (files.TryGetValue(candidate, out var current))
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
