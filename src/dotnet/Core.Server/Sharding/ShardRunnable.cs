using ActualLab.Resilience;

namespace ActualChat;

public sealed record ShardRunnable : IRunnable
{
    public static readonly IRetryPolicy DefaultRetryPolicy = new RetryPolicy(RetryDelaySeq.Exp(0.1, 1));
    public static readonly IRetryPolicy NoRetryPolicy = new RetryPolicy(1, RetryDelaySeq.Exp(0.1, 1));

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

    // This record relies on referential equality
    public bool Equals(ShardRunnable? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private async Task Run(ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
    {
        ILogger? log = null;
        try {
            await InvokeFunc(lockState, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            log ??= lockState.Dispatcher.Log;
            log.LogError(e, "{Name} failed", Name);
            throw;
        }
    }

    private async Task Run(IRetryPolicy retryPolicy, ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
    {
        ILogger? log = null;
        var tryIndex = 0;
        var retryLogger = (RetryLogger?)null;
        while (true) {
            try {
                await InvokeFunc(lockState, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (!retryPolicy.MustRetry(e, ref tryIndex))
                    throw;

                var delay = retryPolicy.Delays[tryIndex];
                log ??= lockState.Dispatcher.Log;
                retryLogger ??= new RetryLogger(log, Name);
                retryLogger.LogRetry(e, tryIndex, retryPolicy.TryCount, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task InvokeFunc(ShardDispatcher.LockState lockState, CancellationToken cancellationToken)
        => Func switch {
            Func<ShardDispatcher.LockState, CancellationToken, Task> f1 => f1.Invoke(lockState, cancellationToken),
            Func<int, CancellationToken, Task> f2 => f2.Invoke(lockState.ShardIndex, cancellationToken),
            _ => throw StandardError.Internal($"Invalid Implementation type: {Func.GetType()}."),
        };
}
