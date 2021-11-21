namespace ActualChat;

public static class DebugExt
{
    public static IAsyncEnumerable<T> WithLog<T>(
        this IAsyncEnumerable<T> sequence,
        ILogger log,
        string title,
        CancellationToken cancellationToken = default)
        => sequence.WithLog(log, title, i => i?.ToString() ?? "(null)", cancellationToken);

    public static async IAsyncEnumerable<T> WithLog<T>(
        this IAsyncEnumerable<T> sequence,
        ILogger log,
        string title,
        Func<T, string> itemFormatter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var index = 0;
        var isFailed = true;
        try {
            await foreach (var item in sequence.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                log.LogInformation("{Title} sequence: #{Index} = {Item}", title, index++, itemFormatter.Invoke(item));
                yield return item;
            }
            isFailed = false;
        }
        finally {
            log.LogInformation("{Title} sequence: ended, failed = {IsFailed}", title, isFailed);
        }
    }
}
