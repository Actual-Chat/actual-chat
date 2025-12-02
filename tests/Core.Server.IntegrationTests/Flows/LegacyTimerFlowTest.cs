using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// [Collection(nameof(ServerCollection))]
public class LegacyTimerFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(LegacyTimerFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            var flows = services.AddFlows(useMasterFlows: false);
            flows.Add<LegacyTimerFlow>();
        },
    }, @out)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task BasicTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.Get<LegacyTimerFlow>("f0,3");
        f0.Should().NotBeNull();

        var f1 = await flows.Get<LegacyTimerFlow>("f1,2");
        f1.Should().NotBeNull();

        await Task.WhenAll(
            WhenEnded(flows, f0.Id),
            WhenEnded(flows, f1.Id));
    }

    [Fact]
    public async Task KillTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var queues = h.Services.GetRequiredService<IQueues>();

        var f0 = await flows.Get<LegacyTimerFlow>("f0,6");
        f0.Should().NotBeNull();

        // Waiting for the RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().BeInRange(2,4);
        }, DefaultTimeout);

        var f1 = await GetFlow(flows, f0).Require();
        await queues.Enqueue(new LegacyFlowKillEvent(f0.Id, "Die, digital creature!"));

        // Waiting for the flow to end quickly
        var diedQuickly = true;
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            if (flow!.RemainingCount < (f1.RemainingCount - 1))
                diedQuickly = false;
            flow.Step.Should().Be(LegacyFlowSteps.OnEnd);
        }, DefaultTimeout);
        diedQuickly.Should().BeTrue();
    }

    [Fact]
    public async Task ResetTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var queues = h.Services.GetRequiredService<IQueues>();

        var f0 = await flows.Get<LegacyTimerFlow>("f0,5");
        f0.Should().NotBeNull();

        // Waiting for the RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(new LegacyFlowResetEvent(f0.Id));

        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().BeGreaterThan(3);
        }, DefaultTimeout);
    }

    // Private methods

    private Task<TFlow?> GetFlow<TFlow>(
        IFlows flows,
        TFlow exampleFlow,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => GetFlow<TFlow>(flows, exampleFlow.Id, cancellationToken);

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, FlowId flowId, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowData = await flows.TryGetData(flowId, cancellationToken);
        var flow = (TFlow?)flowData?.Flow;
        Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
        return flow;
    }

    private Task WhenEnded(IFlows flows, FlowId flowId, double timeout = 15)
        => ComputedTest.When(async ct => {
            var flow = (LegacyFlow)await GetFlow<Flow>(flows, flowId, ct).Require();
            flow.Step.Should().Be(LegacyFlowSteps.OnEnd);
        }, TimeSpan.FromSeconds(timeout));
}
