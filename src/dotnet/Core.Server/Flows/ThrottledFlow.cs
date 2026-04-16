namespace ActualChat.Flows;

/// <summary>
/// A flow that throttles its execution to at most once per <see cref="ThrottlePeriod"/>.
/// If <see cref="Run"/> fails, it retries up to <see cref="MaxFailCount"/> times.
/// After that many consecutive failures, it advances <see cref="NextRunAt"/>
/// as if the run succeeded, to avoid retrying indefinitely.
/// </summary>
public abstract class ThrottledFlow : Flow<string>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected abstract TimeSpan ThrottlePeriod { get; }
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected virtual TimeSpan RetryDelay => TimeSpan.FromMinutes(1);
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected virtual int MaxFailCount => 5;

    // Persisted state

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int SuccessCount { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public int FailCount { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment NextRunAt { get; set; }

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
        try {
            await Run(cancellationToken).ConfigureAwait(false);
            SuccessCount++;
            FailCount = 0;
            NextRunAt = Hub.SystemNow + ThrottlePeriod;
            Console.Log($"Run() #{SuccessCount} completed in {startedAt.Elapsed.ToShortString()}");
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            FailCount++;
            if (FailCount >= MaxFailCount) {
                NextRunAt = Hub.SystemNow + ThrottlePeriod;
                Console.Log($"Run() failed {FailCount} times, giving up until {NextRunAt}");
                FailCount = 0;
            }
            else {
                Runtime.StageResumeIn(RetryDelay);
                Console.Log($"Run() failed (attempt {FailCount}/{MaxFailCount}): {e.Message}, retrying in {RetryDelay.ToShortString()}");
            }
        }
    }
}
