using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public class TimerFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(TimerFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            var flows = services.AddFlows(useMasterFlows: false, useLegacyFlows: false);
            flows.Add<TimerFlow>();
        },
    }, @out)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task VeryBasicTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.Get<TimerFlow>("f0,2");
        await WhenCompleted(flows, f0.Id);
    }

    [Fact]
    public async Task BasicTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.Get<TimerFlow>("f0,3");
        f0.Should().NotBeNull();

        var f1 = await flows.Get<TimerFlow>("f1,2");
        f1.Should().NotBeNull();

        await Task.WhenAll(
            WhenCompleted(flows, f0.Id),
            WhenCompleted(flows, f1.Id));
    }

    [Fact]
    public async Task ResetTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var queues = h.Services.GetRequiredService<IQueues>();

        var f0 = await flows.Get<TimerFlow>("f0,5");
        f0.Should().NotBeNull();

        // Waiting for the RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(new FlowResumeEvent(f0.Id) { MustReset = true });

        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().BeGreaterThan(3);
        }, DefaultTimeout);

        await WhenCompleted(flows, f0.Id);
    }

    // Private methods

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, TFlow exampleFlow, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = (TFlow?)await flows.TryGet(exampleFlow.Id, cancellationToken);
        Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
        return flow;
    }

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, FlowId flowId, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = (TFlow?)await flows.TryGet(flowId, cancellationToken);
        Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
        return flow;
    }

    private Task WhenCompleted(IFlows flows, FlowId flowId, double timeout = 15)
        => ComputedTest.When(async ct => {
            var flow = await GetFlow<Flow>(flows, flowId, ct).Require();
            flow.UntypedResult.Should().NotBeNull();
        }, TimeSpan.FromSeconds(timeout));
}
