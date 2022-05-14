namespace ActualChat.Collections;

public interface IThreadSafeLruCache<in TKey, TValue> : ILruCache<TKey, TValue>
    where TKey : notnull
{ }
