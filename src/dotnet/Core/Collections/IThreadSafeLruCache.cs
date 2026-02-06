namespace ActualChat.Collections;

/// <summary>
/// Thread-safe LRU cache with atomic get-or-add operations.
/// </summary>
public interface IThreadSafeLruCache<TKey, TValue> : ILruCache<TKey, TValue>
    where TKey : notnull
{
    TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
}
