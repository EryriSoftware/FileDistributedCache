using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class FileCacheMetadata(EvictionPolicy policy)
{
    private long _size;
    public long Size => Interlocked.Read(ref _size);
    private object queueLock = new ();
    private readonly ConcurrentDictionary<string, FileCacheEntry> files = new (StringComparer.OrdinalIgnoreCase);
    private readonly PriorityQueue<PriorityCandidate, PriorityDateTimeOffset> lruQueue = new ();
    private readonly PriorityQueue<PriorityCandidate, int> lfuQueue = new ();
    private readonly PriorityQueue<PriorityCandidate, PriorityDateTimeOffset> ttlQueue = new ();
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

    public bool TryGetValue(string key, [NotNullWhen(true)] out FileCacheEntry? value) => files.TryGetValue(key, out value);
    public bool TryUpdate(string key, FileCacheEntry newValue, FileCacheEntry comparisonValue)
    {
        newValue = newValue with
        {
            Version = comparisonValue.Version + 1
        };
        if (files.TryUpdate(key, newValue, comparisonValue))
        {
            Enqueue(newValue);
            Interlocked.Add(ref _size, newValue.SizeBytes - comparisonValue.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(string key, [MaybeNullWhen(false)] out FileCacheEntry value)
    {
        if (files.TryRemove(key, out value))
        {
            Interlocked.Add(ref _size, -value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryRemove(KeyValuePair<string, FileCacheEntry> value)
    {
        if (files.TryRemove(value))
        {
            Interlocked.Add(ref _size, -value.Value.SizeBytes);
            return true;
        }

        return false;
    }

    public bool TryAdd(string key, FileCacheEntry value)
    {
        if (files.TryAdd(key, value))
        {
            Enqueue(value);
            Interlocked.Add(ref _size, value.SizeBytes);
            return true;
        }

        return false;
    }

    private void Enqueue(FileCacheEntry value)
    {
        var candidate = new PriorityCandidate(Key: value.Key, Version: value.Version);
        lock (queueLock)
        {
            ttlQueue.Enqueue(candidate, new PriorityDateTimeOffset(value.Expiration ?? DateTimeOffset.MaxValue));
            switch (policy)
            {
                case EvictionPolicy.TTL:
                    break; // TTL queue is always tracked
                case EvictionPolicy.LRU:
                    lruQueue.Enqueue(candidate, new PriorityDateTimeOffset(value.LastAccessUtc));
                    break;
                case EvictionPolicy.LFU:
                    lfuQueue.Enqueue(candidate, value.AccessCount);
                    break;
                case EvictionPolicy.FIFO:
                    fifoQueue.Enqueue(candidate);
                    break;
                default:
                    throw new NotImplementedException($"Eviction policy {policy} is not implemented.");
            }
        }
    }

    public void EnqueueTtl(FileCacheEntry value)
    {
        var candidate = new PriorityCandidate(Key: value.Key, Version: value.Version);
        lock (queueLock)
        {
            ttlQueue.Enqueue(candidate, new PriorityDateTimeOffset(value.Expiration ?? DateTimeOffset.MaxValue));
        }
    }

    private bool TryDequeueCandidate(EvictionPolicy policy, out PriorityCandidate candidate)
    {
        lock (queueLock)
        {
            return policy switch
            {
                EvictionPolicy.TTL => ttlQueue.TryDequeue(out candidate, out _),
                EvictionPolicy.LRU => lruQueue.TryDequeue(out candidate, out _),
                EvictionPolicy.LFU => lfuQueue.TryDequeue(out candidate, out _),
                EvictionPolicy.FIFO => fifoQueue.TryDequeue(out candidate),
                _ => throw new ArgumentOutOfRangeException(nameof(policy))
            };
        }
    }

    private bool TryDequeue(EvictionPolicy policy, [NotNullWhen(true)] out FileCacheEntry? value)
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

    public bool TryDequeueTTL([NotNullWhen(true)] out FileCacheEntry? value) => TryDequeue(EvictionPolicy.TTL, out value);
    public bool TryDequeueEvictionPolicy([NotNullWhen(true)] out FileCacheEntry? value) => TryDequeue(policy, out value);
}
