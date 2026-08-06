using ActualChat.Queues;

namespace ActualChat.Testing.Host;

public static class QueuesTestExt
{
    public static async Task PurgeWithTimeout(
        this IQueues queues,
        TimeSpan timeout,
        Action<string>? log = null)
    {
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(timeout);
        try {
            await queues.Purge(cts.Token).ConfigureAwait(false);
            if (sw.Elapsed > TimeSpan.FromSeconds(1))
                log?.Invoke($"Queues.Purge took {sw.Elapsed.ToShortString()}");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            log?.Invoke($"Queues.Purge TIMED OUT after {sw.Elapsed.ToShortString()} (limit {timeout.ToShortString()})");
        }
    }
}
