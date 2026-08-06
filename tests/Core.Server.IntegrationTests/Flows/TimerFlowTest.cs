using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualLab.Fusion.Client;
using ActualLab.Resilience;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// [Collection(nameof(ServerCollection))]
[Trait("Category", "Slow")]
public class TimerFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(TimerFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            var flows = services.AddFlows(useMasterFlows: false);
            flows.Add<TimerFlow>();
            var chaosMakerStopsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(15);
            var chaosMaker = (0.75 * ChaosMaker.TransientError)
                .Delayed(new RandomTimeSpan(2, 0.75))
                .Filtered("ShardOwners only && Now <= T",
                    x => chaosMakerStopsAt.Elapsed < TimeSpan.Zero
                        && x is MeshLockHolder h
                        && h.FullKey.Contains("ShardOwner"))
                .Gated(isEnabled: !TestRunnerInfo.IsBuildAgent());
            services.AddSingleton<ChaosMaker>(chaosMaker);
        },
    }, @out)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    [FlakyFact("AY: Slow on GitHub", 3, Timeout = 60_000)]
    public async Task BasicTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var h0 = await NewAppHost();
        await using var h1 = await NewAppHost(o => o with { MustInitializeDb = false });
        var h0node = h0.Services.MeshWatcher().ThisNode;
        var h1node = h1.Services.MeshWatcher().ThisNode;
        WriteLine($"h0.ThisNode: {h0node}");
        WriteLine($"h1.ThisNode: {h1node}");

        var flowHub = h0.Services.FlowHub();

        var flowDef = flowHub.Defs.Get<TimerFlow>();
        flowDef.DataVersion.Should().Be(2);
        flowDef.ResumeTimeout.Should().Be(TimeSpan.FromSeconds(60));

        var command = flowHub.NewResumeEvent<TimerFlow>("f0,1");
        var queueRef = QueueRef.For(command, h0.Services);
        queueRef.ShardScheme.Should().Be(ShardScheme.SlowQueue); // See [Flow] attribute on TimerFlow

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},2", cancellationToken);
        WriteLine($"f0.Id: {f.Id}");

        await WhenCompleted(flowHub, f.Id);
    }

    [FlakyFact("AY: Slow on GitHub", 3, Timeout = 60_000)]
    public async Task TwoFlowsTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var h0 = await NewAppHost();
        await using var h1 = await NewAppHost(o => o with { MustInitializeDb = false });
        WriteLine($"h0.ThisNode: {h0.Services.MeshWatcher().ThisNode}");
        WriteLine($"h1.ThisNode: {h1.Services.MeshWatcher().ThisNode}");

        var flowHub = h0.Services.FlowHub();

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},2", cancellationToken);
        f.Should().NotBeNull();
        var g = await GetLocalFlow<TimerFlow>(flowHub, i => $"g{i},2", cancellationToken);
        g.Should().NotBeNull();

        await Task.WhenAll(
            WhenCompleted(flowHub, f.Id),
            WhenCompleted(flowHub, g.Id));
    }

    [FlakyFact("AY: Slow on GitHub", 3, Timeout = 60_000)]
    public async Task ResetTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var h0 = await NewAppHost();
        await using var h1 = await NewAppHost(o => o with { MustInitializeDb = false });
        WriteLine($"h0.ThisNode: {h0.Services.MeshWatcher().ThisNode}");
        WriteLine($"h1.ThisNode: {h1.Services.MeshWatcher().ThisNode}");

        var flowHub = h0.Services.FlowHub();
        var queues = h0.Services.Queues();

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},5", cancellationToken);
        f.Should().NotBeNull();

        // Waiting for the RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow<TimerFlow>(flowHub, f.Id, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(flowHub.NewResumeEvent(f.Id).WithReset(), cancellationToken);

        await ComputedTest.When(async ct => {
            var flow = await GetFlow<TimerFlow>(flowHub, f.Id, ct);
            flow!.RemainingCount.Should().BeGreaterThan(3);
        }, DefaultTimeout);

        await WhenCompleted(flowHub, f.Id);
    }

    // Private methods

    private async Task<TFlow> GetLocalFlow<TFlow>(FlowHub hub, Func<int, string> argumentFactory, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        FlowId flowId;
        Computed<IFlowData?> cFlowData;
        for (var i = 0;; i++) {
            flowId = hub.NewId<TimerFlow>(argumentFactory.Invoke(i));
            cFlowData = await Computed.Capture(() => hub.Backend.TryGetData(flowId, cancellationToken), cancellationToken);
            cFlowData.Value.Should().BeNull();
            if (cFlowData is not IRemoteComputed)
                break; // We need a remote flow
        }
        var flow = await hub.Get<TFlow>(flowId.Arguments, cancellationToken); // Starts the flow
        cFlowData.IsConsistent().Should().BeFalse();
        return flow;
    }

    private async Task<TFlow> GetRemoteFlow<TFlow>(FlowHub hub, Func<int, string> argumentFactory, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        FlowId flowId;
        Computed<IFlowData?> cFlowData;
        for (var i = 0;; i++) {
            flowId = hub.NewId<TimerFlow>(argumentFactory.Invoke(i));
            cFlowData = await Computed.Capture(() => hub.Backend.TryGetData(flowId, cancellationToken), cancellationToken);
            cFlowData.Value.Should().BeNull();
            if (cFlowData is IRemoteComputed)
                break; // We need a remote flow
        }
        var flow = await hub.Get<TFlow>(flowId.Arguments, cancellationToken); // Starts the flow
        cFlowData.IsConsistent().Should().BeFalse();
        return flow;
    }

    private async Task<TFlow?> GetFlow<TFlow>(
        FlowHub hub, FlowId flowId, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        var cFlowData = await GetFlowDataComputed(hub, flowId, cancellationToken).ConfigureAwait(false);
        var flowData = await cFlowData.Use(allowInconsistent: true, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flowData?.GetFlow(hub);
    }

    private async Task<Computed<IFlowData?>> GetFlowDataComputed(
        FlowHub hub, FlowId flowId, CancellationToken cancellationToken)
    {
        var cFlowData =  await Computed
            .Capture(() => hub.Backend.TryGetData(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var flow = cFlowData.Value?.GetFlow(hub);
        WriteLine($"[*] {flow?.ToString() ?? "null"} <- {cFlowData}");
        return cFlowData;
    }

    private Task WhenCompleted(FlowHub hub, FlowId flowId)
        => ComputedTest.When(async ct => {
            var c = await GetFlowDataComputed(hub, flowId, ct);
            _ = c.UseUntyped(allowInconsistent: true, ct);
            var flow = c.Value?.GetFlow(hub);
            flow.Require();
            flow.UntypedResult.Should().NotBeNull();
        }, DefaultTimeout);
}
