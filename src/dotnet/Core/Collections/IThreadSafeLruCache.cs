namespace ActualChat.Collections;

public interface IThreadSafeLruCache<TKey, TValue> : ILruCache<TKey, TValue>
    where TKey : notnull
{ }
