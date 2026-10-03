using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Eryri.Extensions.Caching.FileSystem.Models;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class FileCacheManifest(IOptions<FileCacheOptions> options)
{
    private readonly EvictionPolicy policy = options.Value.EvictionPolicy;
    private long _size;
    public long Size => Volatile.Read(ref _size);
    public int Count => files.Count;
    private readonly ConcurrentDictionary<string, FileCacheMetadata> files = new (StringComparer.OrdinalIgnoreCase);
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

    public bool TryGetValue(string key, [NotNullWhen(true)] out FileCacheMetadata? value) => files.TryGetValue(key, out value);
    public bool TryUpdate(string key, FileCacheMetadata newValue, FileCacheMetadata comparisonValue)
    {
        newValue = newValue with
        {
            Version = comparisonValue.Version + 1
        };
        if (files.TryUpdate(key, newValue, comparisonValue))
        {
            Enqueue(newValue, comparisonValue);
            Interlocked.Add(ref _size, newValue.SizeBytes - comparisonValue.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(string key, [MaybeNullWhen(false)] out FileCacheMetadata value)
    {
        if (files.TryRemove(key, out value))
        {
            Interlocked.Add(ref _size, -value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(KeyValuePair<string, FileCacheMetadata> value)
    {
        if (files.TryRemove(value))
        {
            Interlocked.Add(ref _size, -value.Value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryAdd(string key, FileCacheMetadata value)
    {
        if (files.TryAdd(key, value))
        {
            Enqueue(value, null);
            Interlocked.Add(ref _size, value.SizeBytes);
            return true;
        }

        return false;
    }

    private void Enqueue(FileCacheMetadata value, FileCacheMetadata? comparisonValue)
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
                if (value.AccessCount != comparisonValue?.AccessCount)
                {
                    lfuQueue.Enqueue(candidate, value.AccessCount);
                }

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

    public void EnqueueTtl(FileCacheMetadata value)
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

    private bool TryDequeue(EvictionPolicy policy, [NotNullWhen(true)] out FileCacheMetadata? value)
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

    public bool TryDequeueTTL([NotNullWhen(true)] out FileCacheMetadata? value) => TryDequeue(EvictionPolicy.TTL, out value);
    public bool TryDequeueEvictionPolicy([NotNullWhen(true)] out FileCacheMetadata? value) => TryDequeue(policy, out value);
}
