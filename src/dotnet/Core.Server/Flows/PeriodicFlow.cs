using MemoryPack;

namespace ActualChat.Flows;

// Base class for flows that run periodically.
// Implements a simple pattern where Run is called at scheduled intervals.
public abstract class PeriodicFlow : Flow<string>
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan MaxResumeDelay => TimeSpan.FromDays(7);
    [IgnoreDataMember, MemoryPackIgnore]
    protected Moment NextRunAt { get; set; }

    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RunCount { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment LastRunAt { get; protected set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public FlowReadiness LastReadiness { get; protected set; }

    // Overridable methods

    protected virtual ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
        => new(FlowReadiness.Ready);

    protected abstract ValueTask<Moment> GetNextRunAt(CancellationToken cancellationToken);

    protected abstract Task Run(CancellationToken cancellationToken);

    // Implementation

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        LastRunAt = default;
        return default;
    }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = ResumedAt + (readiness.ResumeDelay ?? MaxResumeDelay);
            Console.Log($"Prepare -> {readiness}, will resume at {resumeDelay}");
            Runtime.StageResumeAt(resumeDelay);
            return;
        }

        // Compute the next run time
        var nextRunAt = await GetNextRunAt(cancellationToken).ConfigureAwait(false);
        var nextRunIn = (nextRunAt - ResumedAt).Clamp(TimeSpan.Zero, MaxResumeDelay);
        NextRunAt = ResumedAt + nextRunIn;
        if (NextRunAt > ResumedAt) {
            Console.Log($"ComputeNextRunAt -> {NextRunAt} (in {nextRunIn.ToShortString()}), scheduling resume for that time");
            Runtime.StageResumeAt(nextRunAt);
            return;
        }

        // Run
        var startedAt = CpuTimestamp.Now;
        Console.Log($"Run() #{RunCount + 1} started");
        await Run(cancellationToken).ConfigureAwait(false);
        RunCount++;
        LastRunAt = Hub.SystemNow;
        Console.Log($"Run() #{RunCount} completed in {startedAt.Elapsed.ToShortString()}");

        // Schedule the next resume
        Console.Log("Scheduling immediate resume");
        Runtime.StageResume();
    }
}
