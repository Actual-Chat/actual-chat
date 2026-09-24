using ActualChat.Queues;

namespace ActualChat.Testing.Host;

public static class QueuesTestExt
{
    public static async Task WhenStarted(
        this IQueues queues,
        TimeSpan? timeout = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        foreach (var queueRef in queues.Processors.Keys)
            await queues.Services.ShardOwner(queueRef.ShardScheme)
                .WhenOwned(timeout, callerFilePath, callerLine)
                .ConfigureAwait(false);
        // A zero gap makes WhenProcessing complete once every processor has issued its first fetch
        var whenFetching = queues.WhenProcessing(TimeSpan.Zero);
        await TestWait.WhenPolled(
            () => whenFetching.IsCompleted.Should().BeTrue("every queue processor must start fetching"),
            timeout, callerFilePath: callerFilePath, callerLine: callerLine
            ).ConfigureAwait(false);
    }

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
