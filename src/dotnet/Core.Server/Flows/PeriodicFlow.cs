using ActualChat.Time;
using MemoryPack;

namespace ActualChat.Flows;

// Base class for flows that run periodically.
// Implements a simple pattern where Run is called at scheduled intervals.
public abstract class PeriodicFlow : Flow<string>, IQuantProvider
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan MaxResumeDelay => TimeSpan.FromDays(7);

    // Persisted state
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RunCount { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment LastRunAt { get; protected set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public FlowReadiness LastReadiness { get; protected set; }

    // Overridable methods

    protected abstract ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken);
    protected abstract ValueTask<Moment> Run(CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        var flowDef = Hub.Defs.ByType[GetType()];
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = readiness.ResumeDelay ?? MaxResumeDelay;
            var resumeAt = ResumedAt + resumeDelay;
            var resumeQuanta1 = flowDef.GetQuant!(resumeDelay);
            Console.Log($"Prepare() -> {readiness}, will resume at {resumeAt} mod {resumeQuanta1.ToShortString()}");
            Runtime.StageResumeAt(resumeAt, resumeQuanta1);
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
        var resumeQuanta2 = flowDef.GetQuant!(nextRunIn);
        Console.Log($"Next run scheduled at {scheduledAt} (in {nextRunIn.ToShortString()} mod {resumeQuanta2.ToShortString()})");
        Runtime.StageResumeAt(scheduledAt, resumeQuanta2);
    }
}
