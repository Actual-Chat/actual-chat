using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Flow(DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class ResumeLatencyFlow : Flow<Unit>
{
    private const int ResumeCount = 5;
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(1);
    [DataMember(Order = 0)]
    public int RemainingCount { get; set; }
    [DataMember(Order = 1)]
    public Moment LastResumeAt { get; set; }
    [DataMember(Order = 2)]
    public TimeSpan[] Delays { get; set; } = [];

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
            Delays = [..Delays, delay];
            Console.Log($"Resume #{ResumeCount - RemainingCount}: delay={delay.ToShortString()}");
        }
        LastResumeAt = now;

        if (RemainingCount > 0) {
            RemainingCount--;
            Runtime.StageResumeIn(ResumeDelay);
        }
        else {
            Console.Log($"Completed. Delays={Delays.Select(x => x.ToShortString()).ToDelimitedString()}");
            SetResult(default);
        }
        return default;
    }
}
