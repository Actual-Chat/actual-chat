namespace ActualChat;

public static class EnumerableExt
{
    public static bool StartsWith<T>(this IEnumerable<T> left, IReadOnlyCollection<T> right)
        => left.Take(right.Count).SequenceEqual(right);

    public static IEnumerable<T> SkipNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source.Where(x => x != null)!;

    public static IEnumerable<T> NoNullItems<T>(this IEnumerable<T?> source)
        where T : class
        => source!;
}
