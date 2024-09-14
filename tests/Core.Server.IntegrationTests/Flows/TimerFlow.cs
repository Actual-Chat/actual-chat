using System.Globalization;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TimerFlow : Flow
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public int RemainingCount { get; private set; }

    public override FlowOptions GetOptions()
        => new() { RemoveDelay = TimeSpan.FromSeconds(1) };

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var sRemainingCount = Id.Arguments.Split(':', 2).ElementAtOrDefault(1) ?? "1";
        RemainingCount = int.Parse(sRemainingCount, CultureInfo.InvariantCulture);

        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}`.{nameof(OnReset)}: {RemainingCount}");
        return Wait(nameof(OnTimer)).AddTimerEvent(TimeSpan.FromSeconds(3));
    }

    protected async Task<FlowTransition> OnTimer(CancellationToken cancellationToken)
    {
        Event.Require<FlowTimerEvent>();
        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}`.{nameof(OnTimer)}: {RemainingCount--}");
        return RemainingCount > 0
            ? Wait(nameof(OnTimer)).AddTimerEvent(TimeSpan.FromSeconds(5))
            : GotoEnding();
    }

    protected override ValueTask ApplyTransition(
        FlowTransition transition, IFlowEvent @event, CancellationToken cancellationToken)
    {
        var output = Host.Services.GetRequiredService<ITestOutputHelper>();
        output.WriteLine($"`{Id}` transition @ '{Step}': {transition}");
        return base.ApplyTransition(transition, @event, cancellationToken);
    }
}
