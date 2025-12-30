using ActualChat.Time;
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

    protected virtual ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken) => new(FlowReadiness.Ready);
    protected abstract ValueTask<Moment> GetNextRunAt(CancellationToken cancellationToken);
    protected abstract Task Run(CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        LastReadiness = await Prepare(cancellationToken).ConfigureAwait(false);
        if (LastReadiness is { IsSuspended: true } readiness) {
            var resumeDelay = readiness.ResumeDelay ?? MaxResumeDelay;
            var resumeAt = ResumedAt + resumeDelay;
            var resumeQuanta = GetResumeQuanta(resumeDelay);
            Console.Log($"Prepare() -> {readiness}, will resume at {resumeAt} mod {resumeQuanta.ToShortString()}");
            Runtime.StageResumeAt(resumeAt, resumeQuanta);
            return;
        }

        // Compute the next run time
        var nextRunAt = await GetNextRunAt(cancellationToken).ConfigureAwait(false);
        if (nextRunAt == Moment.MaxValue) {
            Console.Log("GetNextRunAt() -> Moment.MaxValue (never)");
            return;
        }
        var nextRunIn = (nextRunAt - ResumedAt).Clamp(TimeSpan.Zero, MaxResumeDelay);
        NextRunAt = ResumedAt + nextRunIn;
        if (NextRunAt > ResumedAt) {
            var resumeQuanta = GetResumeQuanta(nextRunIn);
            Console.Log($"GetNextRunAt() -> {NextRunAt} (in {nextRunIn.ToShortString()} mod {resumeQuanta.ToShortString()}), scheduling resume for that time");
            Runtime.StageResumeAt(NextRunAt, resumeQuanta);
            return;
        }

        // Run
        var startedAt = CpuTimestamp.Now;
        Console.Log($"Run() #{RunCount + 1} started");
        await Run(cancellationToken).ConfigureAwait(false);
        RunCount++;
        LastRunAt = Hub.SystemNow;
        Console.Log($"Run() #{RunCount} completed in {startedAt.Elapsed.ToShortString()}");

        // Schedule the next resume immediately
        //TODO(AK): Probably we can schedule proper delay there, but I don't like code duplication and increasing complexity
        Console.Log("Scheduling immediate resume");
        Runtime.StageResume();
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
