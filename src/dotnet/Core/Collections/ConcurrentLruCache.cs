using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Collections;

public class ConcurrentLruCache<TKey, TValue> : IThreadSafeLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _cacheIndexMask;
    private readonly LruCache<TKey, TValue>[] _caches;

    public ConcurrentLruCache(int capacity, int cacheCount = 0, IEqualityComparer<TKey>? comparer = null)
    {
        if (cacheCount <= 0)
            cacheCount = HardwareInfo.ProcessorCountPo2;
        if (!Bits.IsPowerOf2((ulong)cacheCount))
            throw new ArgumentOutOfRangeException(nameof(cacheCount));

        var capacityPerCache = Math.Max(1, capacity / cacheCount);
        _caches = new LruCache<TKey, TValue>[cacheCount];
        _cacheIndexMask = cacheCount - 1;
        for (var i = 0; i < _caches.Length; i++)
            _caches[i] = new LruCache<TKey, TValue>(capacityPerCache, comparer);
    }

    public int Capacity
        => _caches[0].Capacity * _caches.Length;

    public int Count {
        get {
            int count = 0;
            foreach (var cache in _caches)
                lock (cache) count += cache.Count;
            return count;
        }
    }

    public TValue this[TKey key] {
        get {
            var cache = GetCache(key);
            lock (cache) return cache[key];
        }
        set {
            var cache = GetCache(key);
            lock (cache) cache[key] = value;
        }
    }

    public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        var cache = GetCache(key);
        lock (cache) return cache.TryGetValue(key, out value);
    }

    public TValue? GetValueOrDefault(TKey key)
    {
        var cache = GetCache(key);
        lock (cache) return cache.GetValueOrDefault(key);
    }

    public bool TryAdd(TKey key, TValue value)
    {
        var cache = GetCache(key);
        lock (cache) return cache.TryAdd(key, value);
    }

    public void Add(TKey key, TValue value)
    {
        var cache = GetCache(key);
        lock (cache) cache.Add(key, value);
    }

    public bool Remove(TKey key)
    {
        var cache = GetCache(key);
        lock (cache) return cache.Remove(key);
    }

    public void Clear()
    {
        foreach (var cache in _caches)
            lock (cache) cache.Clear();
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> List(bool recentFirst = false)
        => throw StandardError.NotSupported($"'{GetType().GetName()}' doesn't support this method.");

    // Private methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LruCache<TKey, TValue> GetCache(TKey key)
        => _caches[key.GetHashCode() & _cacheIndexMask];
}
