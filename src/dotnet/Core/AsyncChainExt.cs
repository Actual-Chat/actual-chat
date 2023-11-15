namespace ActualChat;

public static class AsyncChainExt
{
    public static AsyncChain From(
        Func<CancellationToken, Task> start,
        [CallerArgumentExpression(nameof(start))]
        string name = "")
        => new (name, start);
}
