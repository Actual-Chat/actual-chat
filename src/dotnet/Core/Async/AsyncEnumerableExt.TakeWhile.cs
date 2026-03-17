namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static async IAsyncEnumerable<T> TakeWhile<T>(
        this IAsyncEnumerable<T> source,
        Task whileTask,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (whileTask.IsCompleted)
            yield break;

        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try {
            var hasNextTask = enumerator.MoveNextAsync();
            while (true) {
                if (!hasNextTask.IsCompleted)
                    await Task.WhenAny(whileTask, hasNextTask.AsTask()).ConfigureAwait(false);

                if (whileTask.IsCompleted || !await hasNextTask.ConfigureAwait(false))
                    yield break;

                yield return enumerator.Current;

                hasNextTask = enumerator.MoveNextAsync();
            }
        }
        finally {
            await enumerator.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }
}
