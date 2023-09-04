namespace ActualChat;

public static class AsyncChainExt
{
    public static Task Run(
        this IEnumerable<AsyncChain> chains,
        CancellationToken cancellationToken = default)
        => chains.Run(false, cancellationToken);

    public static Task RunIsolated(
        this IEnumerable<AsyncChain> chains,
        CancellationToken cancellationToken = default)
        => chains.Run(true, cancellationToken);

    public static Task Run(
        this IEnumerable<AsyncChain> chains,
        bool isolate,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();
        using (isolate ? ExecutionContextExt.SuppressFlow() : default)
            foreach (var chain in chains)
                tasks.Add(chain.Run(cancellationToken));
        return Task.WhenAll(tasks);
    }

    public static AsyncChain From(
        Func<CancellationToken, Task> start,
        [CallerArgumentExpression(nameof(start))] string name = "")
        => new (name, start);
}
