using System.Globalization;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
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

    protected override async Task Resume(FlowRuntime runtime, CancellationToken cancellationToken)
    {
        if (!IsInitialized) {
            var args = Id.SplitArguments("", "1", "1");
            RemainingCount = int.Parse(args[1], CultureInfo.InvariantCulture);
            Period = TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture));
            IsInitialized = true;
        }

        var output = runtime.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"-> {this}.{nameof(Resume)}: {RemainingCount}");
        var resumeEvent = RemainingCount > 0
            ? runtime.NewResumeEvent(Period, Period / 2)
            : null;
        if (resumeEvent is null)
            Complete(default);
        else
            RemainingCount--;
        await runtime.Store(resumeEvent, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"<- {this}.{nameof(Resume)}: {RemainingCount}");
    }
}
