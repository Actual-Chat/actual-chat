namespace ActualChat;

public sealed class ShardRunnable : IRunnable
{
    public static readonly RetryDelaySeq DefaultRetryDelays = RetryDelaySeq.Exp(0.1, 1);

    public string Name { get; }
    public Delegate Func { get; }
    public RetryDelaySeq? RetryDelays { get; init; } = DefaultRetryDelays; // null means no retries

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
        return RetryDelays is { } retryDelays
            ? Run(retryDelays, shardLockState, cancellationToken)
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

    private async Task Run(RetryDelaySeq retryDelays, ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
    {
        var log = lockState.Dispatcher.Log;
        var retryTracker = new RetryTracker(retryDelays);
        while (true) {
            try {
                await InvokeFunc(lockState, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (!retryTracker.WillRetry(e))
                    throw;

                var delay = retryTracker.Delay;
                log.LogWarning(e, "{Name}: will retry (#{Count}) in {Delay}",
                    Name, retryTracker.Count, delay.ToShortString());
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
