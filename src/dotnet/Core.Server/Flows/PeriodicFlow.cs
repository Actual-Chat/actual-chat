using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

// Base class for flows that run periodically.
// Implements a simple pattern where Run is called at scheduled intervals.
public abstract class PeriodicFlow : Flow<string>, IStuckDetectingFlow
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected virtual TimeSpan MaxResumeDelay => TimeSpan.FromDays(7);

    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int RunCount { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public Moment LastRunAt { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public FlowReadiness LastReadiness { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public Moment NextRunAt { get; set; }

    public virtual bool IsStuck(FlowResumeEvent? nextResumeEvent)
        // NextRunAt is Moment.MaxValue once Run parks the flow: it waits for an external resume, which
        // may never come. Any other one must have its next run pending.
        => nextResumeEvent is null && NextRunAt != Moment.MaxValue;

    // Overridable methods

    protected abstract ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken);
    protected abstract ValueTask<Moment> Run(CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = readiness.ResumeDelay ?? MaxResumeDelay;
            var resumeAt = ResumedAt + resumeDelay;
            NextRunAt = resumeAt;
            Console.Log($"Prepare() -> {readiness}, will resume in {resumeDelay.ToShortString()} mod {FlowDef.DelayQuanta.ToShortString("auto")}");
            Runtime.StageResumeAt(resumeAt);
            return;
        }

        // Run
        var startedAt = CpuTimestamp.Now;
        Console.Log($"Run() #{RunCount + 1} started");
        var nextRunAt = await Run(cancellationToken).ConfigureAwait(false);
        RunCount++;
        LastRunAt = Hub.SystemNow;
        Console.Log($"Run() #{RunCount} completed in {startedAt.Elapsed.ToShortString()}");

        if (nextRunAt == Moment.MaxValue) {
            Console.Log("Run() -> Moment.MaxValue (never run again)");
            NextRunAt = Moment.MaxValue;
            return;
        }

        if (nextRunAt <= Hub.SystemNow) {
            Console.Log("Run() requested immediate resume");
            NextRunAt = Hub.SystemNow;
            Runtime.StageResume();
            return;
        }

        var nextRunIn = (nextRunAt - Hub.SystemNow).Clamp(TimeSpan.Zero, MaxResumeDelay);
        var scheduledAt = Hub.SystemNow + nextRunIn;
        NextRunAt = scheduledAt;
        Console.Log($"Next run scheduled in {nextRunIn.ToShortString()} mod {FlowDef.DelayQuanta.ToShortString("auto")}");
        Runtime.StageResumeAt(scheduledAt);
    }
}
