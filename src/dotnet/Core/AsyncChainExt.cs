namespace ActualChat;

public static class AsyncChainExt
{
    private static TerminalErrorDetector Detector { get; }
        = static (e, ct) => e.IsCancellationOf(ct) || e.Any(IsServiceProviderDisposedException);

    public static AsyncChain From(
        Func<CancellationToken, Task> start,
        [CallerArgumentExpression(nameof(start))]
        string name = "")
        => new (name, start, Detector);

    private static bool IsServiceProviderDisposedException(Exception error)
    {
        if (Equals(error.GetType().Name, "JSDisconnectedException"))
            return true; // This is specific to Blazor Server, it also indicates the scope is going to die soon

        if (error is not ObjectDisposedException ode)
            return false;

        return ode.ObjectName.Contains("IServiceProvider", StringComparison.Ordinal)
            || ode.Message.Contains("'IServiceProvider'", StringComparison.Ordinal);
    }
}
