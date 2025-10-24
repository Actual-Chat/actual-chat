using System.Globalization;
using ActualChat.Flows;
using ActualLab.Generators;
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

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        if (!IsInitialized) {
            IsInitialized = true;
            var args = Id.SplitArguments("", "1", "1");
            RemainingCount = int.Parse(args[1], CultureInfo.InvariantCulture);
            Period = TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture));
            Console.Log($"Initialized: RemainingCount={RemainingCount}, Period={Period.ToShortString()}");
        }
        Runtime.DefaultResumeDelayQuanta = Period / 2;

        if (RemainingCount > 0) {
            RemainingCount--;
            Runtime.ScheduleResumeIn(Period);
            Console.Log($"Will resume in {Period.ToShortString()}, RemainingCount={RemainingCount}");
            if (RemainingCount == 1) {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                await Runtime.Commit(cancellationToken).ConfigureAwait(false);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                Console.Log("Post-commit message");
            }
        }
        else
            SetResult(default);
    }
}
