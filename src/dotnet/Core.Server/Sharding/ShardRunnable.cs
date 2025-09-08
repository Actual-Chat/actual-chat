using ActualLab.Resilience;

namespace ActualChat;

public sealed class ShardRunnable : IRunnable
{
    public static readonly IRetryPolicy DefaultRetryPolicy = new RetryPolicy(RetryDelaySeq.Exp(0.1, 1));

    public string Name { get; }
    public Delegate Func { get; }
    public IRetryPolicy? RetryPolicy { get; init; } = DefaultRetryPolicy; // null means no retries

    private ShardRunnable(string name, Delegate func)
    {
        Name = name;
        Func = func;
    }

    public ShardRunnable(string name, Func<ShardDispatcher.LockState, CancellationToken, Task> func)
        : this(name, (Delegate)func)
    { }

    public ShardRunnable(string name, Func<int, CancellationToken, Task> func)
        : this(name, (Delegate)func)
    { }

    public Task Run(IRunnableRunner runner, CancellationToken cancellationToken)
    {
        var shardLockState = (ShardDispatcher.LockState)runner;
        return RetryPolicy is { } retryPolicy
            ? Run(retryPolicy, shardLockState, cancellationToken)
            : Run(shardLockState, cancellationToken);
    }

    // Private methods

    private Task Run(ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
    {
        var log = lockState.Dispatcher.Log;
        try {
            return InvokeFunc(lockState, cancellationToken).WithErrorLog(cancellationToken, log, "{Name} failed", Name);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            log.LogError(e, "{Name} failed", Name);
            return Task.FromException(e);
        }
    }

    private async Task Run(IRetryPolicy retryPolicy, ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
    {
        var log = lockState.Dispatcher.Log;
        var tryIndex = 0;
        var retryLogger = (RetryLogger?)null;
        while (true) {
            try {
                await InvokeFunc(lockState, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (!retryPolicy.MustRetry(e, ref tryIndex))
                    throw;

                var delay = retryPolicy.GetDelay(tryIndex);
                retryLogger ??= new RetryLogger(log, Name);
                retryLogger.LogRetry(e, tryIndex, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task InvokeFunc(ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
        => Func switch {
            Func<ShardDispatcher.LockState, CancellationToken, Task> f1 => f1.Invoke(lockState, cancellationToken),
            Func<int, CancellationToken, Task> f2 => f2.Invoke(lockState.ShardIndex, cancellationToken),
            _ => throw StandardError.Internal($"Invalid Implementation type: {Func.GetType()}.")
        };
}
