using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Flow(DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ResumeLatencyFlow : Flow<Unit>
{
    private const int ResumeCount = 5;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RemainingCount { get; private set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment LastResumeAt { get; private set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public TimeSpan MaxDelay { get; private set; }

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        RemainingCount = ResumeCount;
        LastResumeAt = Hub.SystemNow;
        Console.Log($"Init: RemainingCount={RemainingCount}");
        return default;
    }

    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        var now = Hub.SystemNow;
        if (LastResumeAt != default) {
            var delay = now - LastResumeAt;
            if (delay > MaxDelay)
                MaxDelay = delay;
            Console.Log($"Resume #{ResumeCount - RemainingCount}: delay={delay.ToShortString()}");
        }
        LastResumeAt = now;

        if (RemainingCount > 0) {
            RemainingCount--;
            Runtime.StageResumeIn(TimeSpan.FromSeconds(1));
        }
        else {
            Console.Log($"Completed. MaxDelay={MaxDelay.ToShortString()}");
            SetResult(default);
        }
        return default;
    }
}
