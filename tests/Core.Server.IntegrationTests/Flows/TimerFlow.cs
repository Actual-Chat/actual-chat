using System.Globalization;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TimerFlow : Flow<Unit>
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public bool IsInitialized { get; private set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public int RemainingCount { get; private set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public TimeSpan Period { get; private set; }

    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        if (!IsInitialized) {
            var args = Id.SplitArguments("", "1", "1");
            RemainingCount = int.Parse(args[1], CultureInfo.InvariantCulture);
            Period = TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture));
            IsInitialized = true;
        }
        Runtime.DefaultResumeDelayQuanta = Period / 2;

        var output = Runtime.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"-> {this}.{nameof(Resume)}: {RemainingCount}");
        if (RemainingCount > 0) {
            RemainingCount--;
            Runtime.ScheduleResumeIn(Period);
        }
        else
            SetResult(default);
        output.WriteLine($"<- {this}.{nameof(Resume)}: {RemainingCount}");
        return default;
    }
}
