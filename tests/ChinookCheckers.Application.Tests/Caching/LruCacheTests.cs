using ChinookCheckers.Application.Caching;
using Microsoft.Extensions.Time.Testing;

namespace ChinookCheckers.Application.Tests.Caching;

public class LruCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _clock = new();

    private LruCache<string, int> CreateCache(
        int capacity = 3,
        TimeSpan? ttl = null,
        IEqualityComparer<string>? comparer = null) =>
        new(capacity, ttl ?? Ttl, _clock, comparer);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCache(capacity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_NonPositiveTtl_Throws(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCache(ttl: TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void TryGet_StoredKey_ReturnsValue()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var cache = CreateCache();

        Assert.False(cache.TryGet("missing", out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesValueWithoutGrowing()
    {
        var cache = CreateCache();
        cache.Set("a", 1);
        cache.Set("a", 2);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void Comparer_IsUsedForKeys()
    {
        var cache = CreateCache(comparer: StringComparer.OrdinalIgnoreCase);
        cache.Set("KEY", 1);

        Assert.True(cache.TryGet("key", out _));
    }

    // ---- capacity and LRU eviction ------------------------------------------------------------

    [Fact]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsedEntry()
    {
        var cache = CreateCache(capacity: 3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        cache.Set("d", 4);

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void TryGet_MarksEntryAsRecentlyUsed()
    {
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.TryGet("a", out _);   // "b" is now the least recently used
        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Set_ExistingKey_MarksEntryAsRecentlyUsed()
    {
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Set("a", 10);   // "b" is now the least recently used
        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(10, value);
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void FullCache_AlwaysAdmitsNewEntries()
    {
        var cache = CreateCache(capacity: 1);

        cache.Set("a", 1);
        cache.Set("b", 2);

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
    }

    // ---- time to live ---------------------------------------------------------------------------

    [Fact]
    public void TryGet_BeforeTtlElapsed_ReturnsValue()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        _clock.Advance(Ttl - TimeSpan.FromMilliseconds(1));

        Assert.True(cache.TryGet("a", out _));
    }

    [Fact]
    public void TryGet_WhenTtlElapsed_ReturnsFalseAndRemovesEntry()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        _clock.Advance(Ttl);

        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Ttl_IsAbsolute_ReadsDoNotExtendIt()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True(cache.TryGet("a", out _));

        _clock.Advance(TimeSpan.FromMinutes(5));   // 11 minutes since the entry was stored
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Set_ExistingKey_RestartsTtl()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        _clock.Advance(TimeSpan.FromMinutes(6));
        cache.Set("a", 2);
        _clock.Advance(TimeSpan.FromMinutes(6));   // 12 minutes since the first Set, 6 since the second

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void ExpiredEntries_AreSweptEvenWhenNeverReadAgain()
    {
        var cache = CreateCache();
        cache.Set("a", 1);
        cache.Set("b", 2);

        _clock.Advance(Ttl);
        cache.TryGet("unrelated", out _);   // any call may trigger the periodic sweep

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Sweep_KeepsEntriesThatAreStillAlive()
    {
        var cache = CreateCache();
        cache.Set("old", 1);
        _clock.Advance(TimeSpan.FromMinutes(6));
        cache.Set("fresh", 2);

        _clock.Advance(TimeSpan.FromMinutes(5));   // "old" is 11 minutes old, "fresh" only 5

        Assert.True(cache.TryGet("fresh", out _));
        Assert.False(cache.TryGet("old", out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ExpiredEntry_DoesNotBlockNewEntries()
    {
        var cache = CreateCache(capacity: 1);
        cache.Set("a", 1);
        _clock.Advance(Ttl);

        cache.Set("b", 2);

        Assert.True(cache.TryGet("b", out _));
        Assert.Equal(1, cache.Count);
    }

    // ---- thread safety --------------------------------------------------------------------------

    [Fact]
    public void ConcurrentAccess_KeepsCapacityAndNeverReturnsForeignValues()
    {
        const int capacity = 50;
        var cache = new LruCache<int, int>(capacity, Ttl);

        Parallel.For(0, 8, worker =>
        {
            var random = new Random(worker);
            for (var i = 0; i < 5_000; i++)
            {
                var key = random.Next(200);
                if (cache.TryGet(key, out var value))
                {
                    Assert.Equal(key, value);
                }
                else
                {
                    cache.Set(key, key);
                }
            }
        });

        Assert.InRange(cache.Count, 1, capacity);
    }
}
