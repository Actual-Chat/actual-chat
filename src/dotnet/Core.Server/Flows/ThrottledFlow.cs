using MemoryPack;

namespace ActualChat.Flows;

/// <summary>
/// A flow that throttles its execution to at most once per <see cref="ThrottlePeriod"/>.
/// </summary>
public abstract class ThrottledFlow : Flow<string>
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected abstract TimeSpan ThrottlePeriod { get; }

    // Persisted state

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int SuccessCount { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment NextRunAt { get; protected set; }

    // Overridable methods

    protected virtual ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
        => ValueTask.FromResult(FlowReadiness.Ready);

    protected abstract ValueTask Run(CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        if (ResumedAt < NextRunAt) {
            var remaining = NextRunAt - ResumedAt;
            Console.Log($"Throttled, {remaining.ToShortString()} remaining");
            return; // Too soon, ignore this resume
        }

        var readiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (readiness.IsSuspended) {
            Console.Log($"Prepare() -> {readiness}");
            return; // Not ready, ignore this resume
        }

        // Execute
        var startedAt = CpuTimestamp.Now;
        Console.Log($"Run() #{SuccessCount + 1} started");
        await Run(cancellationToken).ConfigureAwait(false);
        SuccessCount++;
        NextRunAt = Hub.SystemNow + ThrottlePeriod;
        Console.Log($"Run() #{SuccessCount} completed in {startedAt.Elapsed.ToShortString()}");
    }
}
