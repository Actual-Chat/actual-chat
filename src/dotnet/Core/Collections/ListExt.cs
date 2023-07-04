namespace ActualChat.Collections;

public static class ListExt
{
    public static T GetRandom<T>(this IReadOnlyList<T> span)
        => span[Random.Shared.Next(span.Count)];
    public static T GetRandom<T>(this IReadOnlyList<T> span, Random random)
        => span[random.Next(span.Count)];

    public static List<T> AddMany<T>(this List<T> list, T item, int count)
    {
        for (var i = 0; i < count; i++)
            list.Add(item);
        return list;
    }
}
