using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Collections;

public class ThreadSafeLruCache<TKey, TValue> : IThreadSafeLruCache<TKey, TValue>
    where TKey : notnull
{
    public object Lock { get; } = new();
    public LruCache<TKey, TValue> Cache { get; }

    public ThreadSafeLruCache(int capacity) : this(new LruCache<TKey, TValue>(capacity)) { }
    public ThreadSafeLruCache(LruCache<TKey, TValue> cache) => Cache = cache;

    public int Capacity {
        get {
            lock (Lock) return Cache.Capacity;
        }
    }

    public int Count {
        get {
            lock (Lock) return Cache.Count;
        }
    }

    public TValue this[TKey key] {
        get {
            lock (Lock) return Cache[key];
        }
        set {
            lock (Lock) Cache[key] = value;
        }
    }

    public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        lock (Lock) return Cache.TryGetValue(key, out value);
    }

    public TValue? GetValueOrDefault(TKey key)
    {
        lock (Lock) return Cache.GetValueOrDefault(key);
    }

    public bool TryAdd(TKey key, TValue value)
    {
        lock (Lock) return Cache.TryAdd(key, value);
    }

    public void Add(TKey key, TValue value)
    {
        lock (Lock) Cache.Add(key, value);
    }

    public bool Remove(TKey key)
    {
        lock (Lock) return Cache.Remove(key);
    }

    public void Clear()
    {
        lock (Lock) Cache.Clear();
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> List(bool recentFirst = false)
    {
        lock (Lock) return Cache.List(recentFirst).ToList();
    }
}
