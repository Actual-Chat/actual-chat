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

    public static async Task<(T1, T2)> Collect<T1, T2>(this (Task<T1>, Task<T2>) tasks, int chunkSize = -1)
    {
        var (task1, task2) = tasks;
        await new Task[] { task1, task2 }.Collect(chunkSize).ConfigureAwait(false);
        return (task1.Result, task2.Result);
    }

    public static async Task<(T1, T2, T3)> Collect<T1, T2, T3>(this (Task<T1>, Task<T2>, Task<T3>) tasks, int chunkSize = -1)
    {
        var (task1, task2, task3) = tasks;
        await new Task[] { task1, task2 }.Collect(chunkSize).ConfigureAwait(false);
        return (task1.Result, task2.Result, task3.Result);
    }
}
