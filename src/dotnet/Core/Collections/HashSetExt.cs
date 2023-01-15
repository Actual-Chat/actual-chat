namespace ActualChat.Collections;

public static class HashSetExt
{
    public static void AddOrUpdate<T>(this HashSet<T> source, T item)
    {
        source.Remove(item);
        source.Add(item);
    }

    public static ImmutableHashSet<T> AddOrUpdate<T>(this ImmutableHashSet<T> source, T item)
        => source.Remove(item).Add(item);
}
