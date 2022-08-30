namespace ActualChat.Collections;

public static class ListExt
{
    public static T GetRandom<T>(this IReadOnlyList<T> span)
        => span[Random.Shared.Next(span.Count)];
    public static T GetRandom<T>(this IReadOnlyList<T> span, Random random)
        => span[random.Next(span.Count)];

}
