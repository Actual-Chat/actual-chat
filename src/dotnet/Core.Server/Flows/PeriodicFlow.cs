using ActualChat.Time;
using MemoryPack;

namespace ActualChat.Flows;

// Base class for flows that run periodically.
// Implements a simple pattern where Run is called at scheduled intervals.
public abstract class PeriodicFlow : Flow<string>
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

    protected virtual ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken) => new(FlowReadiness.Ready);
    protected abstract ValueTask<Moment> Run(CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = readiness.ResumeDelay ?? MaxResumeDelay;
            var resumeAt = ResumedAt + resumeDelay;
            var resumeQuanta1 = GetResumeQuanta(resumeDelay);
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
        var resumeQuanta2 = GetResumeQuanta(nextRunIn);
        Console.Log($"Next run scheduled at {scheduledAt} (in {nextRunIn.ToShortString()} mod {resumeQuanta2.ToShortString()})");
        Runtime.StageResumeAt(scheduledAt, resumeQuanta2);
    }

    protected virtual TimeSpan GetResumeQuanta(TimeSpan delay)
    {
        if (this is IHasDelayQuanta hasDelayQuanta)
            return hasDelayQuanta.DelayQuanta;

        var delaySeconds = delay.TotalSeconds;
        if (delaySeconds >= 2 * 24 * 3600) // More than 2 days
            return TimeSpan.FromDays(1);
        if (delaySeconds >= 2 * 3600) // More than 2 hours
            return TimeSpan.FromHours(1);
        if (delaySeconds >= 2 * 60) // More than 2 minutes
            return TimeSpan.FromMinutes(1);
        return TimeSpan.FromSeconds(1); // We don't need to run periodic flows more often than every second
    }
}
