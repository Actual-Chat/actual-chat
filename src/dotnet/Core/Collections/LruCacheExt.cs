namespace ActualChat.Collections;

public static class LruCacheExt
{
    public static TValue AddOrGet<TKey, TValue>(
        this ILruCache<TKey, TValue> cache, TKey key, TValue value)
        where TKey : notnull
    {
        if (cache.TryAdd(key, value))
            return value;

        return cache.TryGetValue(key, out var cachedValue) ? cachedValue : value;
    }

    public static TValue GetOrCreate<TKey, TValue>(
        this ILruCache<TKey, TValue> cache, TKey key, Func<TKey, TValue> factory)
        where TKey : notnull
        => cache.TryGetValue(key, out var value)
            ? value
            : cache.AddOrGet(key, factory.Invoke(key));

    public static TValue GetOrCreate<TKey, TValue, TState>(
        this ILruCache<TKey, TValue> cache, TKey key, Func<TState, TValue> factory, TState state)
        where TKey : notnull
        => cache.TryGetValue(key, out var value)
            ? value
            : cache.AddOrGet(key, factory.Invoke(state));

    public static TValue GetOrCreate<TKey, TValue, TState>(
        this ILruCache<TKey, TValue> cache, TKey key, Func<TKey, TState, TValue> factory, TState state)
        where TKey : notnull
        => cache.TryGetValue(key, out var value)
            ? value
            : cache.AddOrGet(key, factory.Invoke(key, state));
}
