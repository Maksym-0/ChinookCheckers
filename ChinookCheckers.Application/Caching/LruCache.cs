using System.Diagnostics.CodeAnalysis;

namespace ChinookCheckers.Application.Caching;

/// <summary>
/// Thread-safe cache bounded by capacity with least-recently-used eviction and an absolute time-to-live.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="_index"/> gives O(1) lookup, <see cref="_recency"/> keeps entries ordered from most recently used
/// (first) to least recently used (last). Every read and write moves the entry to the front; when the capacity
/// is exceeded the last entry is evicted, so a full cache always admits new entries instead of rejecting them.
/// </para>
/// <para>
/// The TTL is absolute: it starts when a value is stored and is not extended by reads. An expired entry is
/// never returned. It is removed when it is read, or by a sweep that runs at most once per
/// <see cref="MaxSweepInterval"/> as part of normal calls (no background thread or timer). Without sweeping,
/// entries that are never read again would stay until LRU eviction pushed them out.
/// </para>
/// <para>All members are safe to call concurrently. A single lock guards both structures, because even a read mutates the recency list.</para>
/// </remarks>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private static readonly TimeSpan MaxSweepInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _index;
    private readonly LinkedList<CacheEntry> _recency = new();
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _sweepInterval;
    private readonly TimeProvider _time;
    private long _lastSweepTimestamp;

    /// <param name="capacity">Maximum number of entries; the least recently used one is evicted beyond it.</param>
    /// <param name="ttl">Lifetime of an entry counted from the moment it was stored.</param>
    /// <param name="timeProvider">Clock source; defaults to <see cref="TimeProvider.System"/>. Replaceable in tests.</param>
    /// <param name="comparer">Key comparer; defaults to the default comparer of <typeparamref name="TKey"/>.</param>
    public LruCache(
        int capacity,
        TimeSpan ttl,
        TimeProvider? timeProvider = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        _capacity = capacity;
        _ttl = ttl;
        _sweepInterval = ttl < MaxSweepInterval ? ttl : MaxSweepInterval;
        _time = timeProvider ?? TimeProvider.System;
        _index = new Dictionary<TKey, LinkedListNode<CacheEntry>>(comparer);
        _lastSweepTimestamp = _time.GetTimestamp();
    }

    /// <summary>
    /// Number of stored entries. May include entries that have expired but were not removed yet.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>Returns a live value and marks it as most recently used; expired entries are removed and reported as missing.</summary>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            SweepIfDue(now);

            if (_index.TryGetValue(key, out var node))
            {
                if (!IsExpired(node.Value, now))
                {
                    MoveToFront(node);
                    value = node.Value.Value;
                    return true;
                }

                Remove(node);
            }
        }

        value = default;
        return false;
    }

    /// <summary>Adds or replaces a value, restarting its TTL and making it the most recently used entry.</summary>
    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            SweepIfDue(now);

            if (_index.TryGetValue(key, out var existing))
            {
                existing.Value.Value = value;
                existing.Value.StoredAt = now;
                MoveToFront(existing);
                return;
            }

            _index.Add(key, _recency.AddFirst(new CacheEntry(key, value, now)));

            if (_index.Count > _capacity)
            {
                Remove(_recency.Last!);
            }
        }
    }

    private void MoveToFront(LinkedListNode<CacheEntry> node)
    {
        if (node != _recency.First)
        {
            _recency.Remove(node);
            _recency.AddFirst(node);
        }
    }

    private void Remove(LinkedListNode<CacheEntry> node)
    {
        _index.Remove(node.Value.Key);
        _recency.Remove(node);
    }

    private bool IsExpired(CacheEntry entry, long now) => _time.GetElapsedTime(entry.StoredAt, now) >= _ttl;

    private void SweepIfDue(long now)
    {
        if (_time.GetElapsedTime(_lastSweepTimestamp, now) < _sweepInterval)
        {
            return;
        }

        _lastSweepTimestamp = now;
        for (var node = _recency.First; node is not null;)
        {
            var next = node.Next;
            if (IsExpired(node.Value, now))
            {
                Remove(node);
            }

            node = next;
        }
    }

    private sealed class CacheEntry(TKey key, TValue value, long storedAt)
    {
        public TKey Key { get; } = key;

        public TValue Value { get; set; } = value;

        public long StoredAt { get; set; } = storedAt;
    }
}
