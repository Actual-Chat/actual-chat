namespace ActualChat.Flows;

// Base class for flows that run periodically.
// Implements a simple pattern where Run is called at scheduled intervals.
public abstract class PeriodicFlow : Flow<string>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected virtual TimeSpan MaxResumeDelay => TimeSpan.FromDays(7);

    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RunCount { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment LastRunAt { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public FlowReadiness LastReadiness { get; set; }

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
            return;
        }

        if (nextRunAt <= Hub.SystemNow) {
            Console.Log("Run() requested immediate resume");
            Runtime.StageResume();
            return;
        }

        var nextRunIn = (nextRunAt - Hub.SystemNow).Clamp(TimeSpan.Zero, MaxResumeDelay);
        var scheduledAt = Hub.SystemNow + nextRunIn;
        Console.Log($"Next run scheduled in {nextRunIn.ToShortString()} mod {FlowDef.DelayQuanta.ToShortString("auto")}");
        Runtime.StageResumeAt(scheduledAt);
    }
}
