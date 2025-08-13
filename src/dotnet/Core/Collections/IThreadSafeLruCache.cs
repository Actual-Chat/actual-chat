namespace ActualChat.Collections;

public interface IThreadSafeLruCache<TKey, TValue> : ILruCache<TKey, TValue>
    where TKey : notnull
{
    TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
}
