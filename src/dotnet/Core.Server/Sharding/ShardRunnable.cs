using ActualLab.Resilience;

namespace ActualChat.Sharding;

public sealed record ShardRunnable : IRunnable
{
    public static readonly IRetryPolicy DefaultRetryPolicy = new RetryPolicy(RetryDelaySeq.Exp(0.1, 1));
    public static readonly IRetryPolicy NoRetryPolicy = new RetryPolicy(1, RetryDelaySeq.Exp(0.1, 1));
    public static readonly RandomTimeSpan DefaultRepeatDelay = TimeSpan.FromMicroseconds(50).ToRandom(0.5);

    public string Name { get; }
    public Delegate Func { get; }
    public IRetryPolicy? RetryPolicy { get; init; } = DefaultRetryPolicy; // null means no retries
    public RandomTimeSpan? RepeatDelay { get; init; } = DefaultRepeatDelay; // null means no repeats on exit
    public MomentClock Clock { get; init; }

    private ShardRunnable(string name, Delegate func, MomentClock clock)
    {
        Name = name;
        Func = func;
        Clock = clock;
    }

    public ShardRunnable(string name, Func<ShardLease, CancellationToken, Task> func, MomentClock clock)
        : this(name, (Delegate)func, clock)
    { }

    public ShardRunnable(string name, Func<int, CancellationToken, Task> func, MomentClock clock)
        : this(name, (Delegate)func, clock)
    { }

    public async Task Run(IRunnableRunner runner, CancellationToken cancellationToken)
    {
        var shardLease = (ShardLease)runner;
        ILogger? log = null;
        var tryIndex = 0;
        var retryLogger = (RetryLogger?)null;
        while (true) {
            try {
                while (true) {
                    await InvokeFunc(shardLease, cancellationToken).ConfigureAwait(false);
                    if (RepeatDelay is not { } repeatDelay)
                        return;

                    await Clock.Delay(repeatDelay.Next(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                log ??= shardLease.ShardBroker.Log;
                if (RetryPolicy is not { } retryPolicy || !retryPolicy.MustRetry(e, ref tryIndex)) {
                    log.LogError(e, "{Name} failed", Name);
                    throw;
                }

                var delay = retryPolicy.Delays[tryIndex];
                retryLogger ??= new RetryLogger(log, Name);
                retryLogger.LogRetry(e, tryIndex, retryPolicy.TryCount, delay);
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // This record relies on referential equality
    public bool Equals(ShardRunnable? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private Task InvokeFunc(ShardLease shardLease, CancellationToken cancellationToken)
        => Func switch {
            Func<ShardLease, CancellationToken, Task> f1 => f1.Invoke(shardLease, cancellationToken),
            Func<int, CancellationToken, Task> f2 => f2.Invoke(shardLease.ShardIndex, cancellationToken),
            _ => throw StandardError.Internal($"Invalid Implementation type: {Func.GetType()}."),
        };
}
