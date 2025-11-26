using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public abstract partial class NewPeriodicFlow : Flow<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan MaxDelay { get; } = TimeSpan.FromDays(7);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Moment LastRunAt { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment? NextRunAt { get; protected set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public int RunCount { get; protected set; }

    protected abstract Task Run(CancellationToken cancellationToken);
    protected abstract Moment ComputeNextRunAt(Moment now);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var now = ActualLab.Time.CpuClock.Now;

        if (NextRunAt.HasValue && NextRunAt.Value > now) {
            Runtime.ScheduleResumeIn(NextRunAt.Value - now);
            return;
        }

        await Run(cancellationToken).ConfigureAwait(false);

        LastRunAt = ActualLab.Time.CpuClock.Now;
        RunCount++;

        var nextRunAt = ComputeNextRunAt(LastRunAt);
        var delay = nextRunAt - LastRunAt;
        if (delay > MaxDelay) delay = MaxDelay;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        NextRunAt = LastRunAt + delay;

        Console.Log($"Run #{RunCount} done. Next run in {delay.ToShortString()} at {NextRunAt}");
        Runtime.ScheduleResumeIn(delay);
    }
}
