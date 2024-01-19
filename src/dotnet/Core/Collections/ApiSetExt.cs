namespace ActualChat.Collections;

public static class ApiSetExt
{
    public static ApiSet<T> With<T>(this ApiSet<T> set, T item)
    {
        var newSet = set.Clone();
        newSet.Add(item);
        return newSet;
    }

    public static ApiSet<T> With<T>(this ApiSet<T> set, params T[] items)
    {
        var newSet = set.Clone();
        newSet.AddRange(items);
        return newSet;
    }

    public static ApiSet<T> Without<T>(this ApiSet<T> set, T item)
    {
        var newSet = set.Clone();
        set.Remove(item);
        return newSet;
    }

    public static ApiSet<T> Without<T>(this ApiSet<T> set, params T[] items)
    {
        var newSet = set.Clone();
        foreach (var item in items)
            newSet.Remove(item);
        return newSet;
    }
}
