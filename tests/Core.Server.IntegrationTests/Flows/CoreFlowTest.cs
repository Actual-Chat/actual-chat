using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public class CoreFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(CoreFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<TimerFlow>();
        },
    }, @out)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TimerFlowTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.GetOrStart<TimerFlow>("f0,3");
        f0.Should().NotBeNull();

        var f1 = await flows.GetOrStart<TimerFlow>("f1,2");
        f1.Should().NotBeNull();

        await Task.WhenAll(
            WhenEnded(flows, f0.Id),
            WhenEnded(flows, f1.Id));
    }

    [Fact]
    public async Task KillFlowTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var queues = h.Services.GetRequiredService<IQueues>();

        var f0 = await flows.GetOrStart<TimerFlow>("f0,5");
        f0.Should().NotBeNull();

        // Waiting for RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(new FlowKillEvent(f0.Id, "Die, digital creature!"));

        // Waiting for flow to end quickly
        var diedQuickly = true;
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            if (flow!.RemainingCount <= 2)
                diedQuickly = false;
            flow.Step.Should().Be(FlowSteps.OnEnd);
        }, DefaultTimeout);
        diedQuickly.Should().BeTrue();
    }

    [Fact]
    public async Task ResetFlowTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var queues = h.Services.GetRequiredService<IQueues>();

        var f0 = await flows.GetOrStart<TimerFlow>("f0,5");
        f0.Should().NotBeNull();

        // Waiting for RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(new FlowResetEvent(f0.Id));

        await ComputedTest.When(async ct => {
            var flow = await GetFlow(flows, f0, ct);
            flow!.RemainingCount.Should().BeGreaterThan(3);
        }, DefaultTimeout);
    }

    // Private methods

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, TFlow exampleFlow, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = (TFlow?)await flows.Get(exampleFlow.Id, cancellationToken);
        Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
        return flow;
    }

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, FlowId flowId, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = (TFlow?)await flows.Get(flowId, cancellationToken);
        Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
        return flow;
    }

    private Task WhenEnded(IFlows flows, FlowId flowId, double timeout = 15)
        => ComputedTest.When(async ct => {
            var flow = await GetFlow<Flow>(flows, flowId, ct);
            flow!.Step.Should().Be(FlowSteps.OnEnd);
        }, TimeSpan.FromSeconds(timeout));
}
