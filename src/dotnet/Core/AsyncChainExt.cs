using Stl.Diagnostics;

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

    public static AsyncChain Log(this AsyncChain asyncChain, ILogger? log)
        => asyncChain.Log(LogLevel.Information, log);
    public static AsyncChain Log(this AsyncChain asyncChain, LogLevel logLevel, ILogger? log)
    {
        if (log == null)
            return asyncChain;

        return asyncChain with {
            Start = async cancellationToken => {
                log.IfEnabled(logLevel)?.Log(logLevel, "AsyncChain started: {ChainName}", asyncChain.Name);
                var error = (Exception?) null;
                try {
                    await asyncChain.Start(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) {
                    error = e;
                    throw;
                }
                finally {
                    if (error == null || IsAlwaysThrowable(error)) {
                        var message = cancellationToken.IsCancellationRequested
                            ? "AsyncChain completed (cancelled): {ChainName}"
                            : "AsyncChain completed: {ChainName}";
                        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                        log.IfEnabled(logLevel)?.Log(logLevel, message, asyncChain.Name);
                    }
                    else
                        log.LogError(error, "AsyncChain failed: {ChainName}", asyncChain.Name);
                }
            }
        };
    }

    public static AsyncChain From(
        Func<CancellationToken, Task> start,
        [CallerArgumentExpression(nameof(start))]
        string name = "")
        => new (name, start);


    private static bool IsAlwaysThrowable(Exception e)
    {
        switch (e) {
        case OperationCanceledException:
        // Special case: this exception can be thrown on IoC container disposal,
        // and if we don't handle it in a special way, DbWakeSleepProcessBase
        // descendants may flood the log with exceptions till the moment they're stopped.
        case ObjectDisposedException ode
            when ode.Message.Contains("'IServiceProvider'", StringComparison.Ordinal):
            return true;
        default:
            return false;
        }
    }
}
