namespace ActualChat.Collections;

public static class ListExt
{
    public static T GetOrDefault<T>(this IReadOnlyList<T> list, int index, T @default = default!)
        => index < 0 ? @default
            : index >= list.Count ? @default
            : list[index];

    public static T GetRandom<T>(this IReadOnlyList<T> list)
        => list[Random.Shared.Next(list.Count)];
    public static T GetRandom<T>(this IReadOnlyList<T> list, Random random)
        => list[random.Next(list.Count)];

    public static List<T> AddMany<T>(this List<T> list, T item, int count)
    {
        for (var i = 0; i < count; i++)
            list.Add(item);
        return list;
    }
}
