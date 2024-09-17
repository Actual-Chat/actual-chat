using System.Globalization;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TimerFlow : Flow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RemainingCount { get; private set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public double Period { get; private set; }

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var args = Id.SplitArguments("", "1", "1");
        RemainingCount = int.Parse(args[1], CultureInfo.InvariantCulture);
        Period = double.Parse(args[2], CultureInfo.InvariantCulture);

        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}`.{nameof(OnReset)}: {RemainingCount}");
        return WaitForTimer(nameof(OnTimer), TimeSpan.FromSeconds(Period));
    }

    protected async Task<FlowTransition> OnTimer(CancellationToken cancellationToken)
    {
        Event.Require<FlowTimerEvent>();
        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}`.{nameof(OnTimer)}: {RemainingCount--}");
        return RemainingCount > 0
            ? WaitForTimer(nameof(OnTimer), TimeSpan.FromSeconds(Period))
            : End($"{nameof(RemainingCount)} is 0");
    }

    protected override ValueTask ApplyTransition(
        FlowTransition transition, IFlowEvent @event, CancellationToken cancellationToken)
    {
        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}` transition @ '{Step}': {transition}");
        return base.ApplyTransition(transition, @event, cancellationToken);
    }
}
