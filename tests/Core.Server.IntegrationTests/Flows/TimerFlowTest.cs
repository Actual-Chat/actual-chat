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
            var flows = services.AddFlows(useMasterFlows: false, useLegacyFlows: false);
            flows.Add<TimerFlow>();
            var chaosMakerStopsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(15);
            var chaosMaker = (0.75 * ChaosMaker.TransientError)
                .Delayed(new RandomTimeSpan(2, 0.75))
                .Filtered("ShardOwners only && Now <= T",
                    x => chaosMakerStopsAt.Elapsed < TimeSpan.Zero
                        && x is MeshLockHolder h
                        && h.FullKey.OrdinalContains("ShardOwner"))
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

        var flows = h0.Services.GetRequiredService<IFlows>();

        var f = await GetRemoteFlow<TimerFlow>(flows, i => $"f{i},2", cancellationToken);
        WriteLine($"f0.Id: {f.Id}");

        await WhenCompleted(flows, f.Id);
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

        var flows = h0.Services.GetRequiredService<IFlows>();

        var f = await GetRemoteFlow<TimerFlow>(flows, i => $"f{i},2", cancellationToken);
        f.Should().NotBeNull();
        var g = await GetLocalFlow<TimerFlow>(flows, i => $"g{i},2", cancellationToken);
        g.Should().NotBeNull();

        await Task.WhenAll(
            WhenCompleted(flows, f.Id),
            WhenCompleted(flows, g.Id));
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

        var flows = h0.Services.GetRequiredService<IFlows>();
        var queues = h0.Services.GetRequiredService<IQueues>();

        var f = await GetRemoteFlow<TimerFlow>(flows, i => $"f{i},5", cancellationToken);
        f.Should().NotBeNull();

        // Waiting for the RemainingCount to hit 3
        await ComputedTest.When(async ct => {
            var flow = await GetFlow<TimerFlow>(flows, f.Id, ct);
            flow!.RemainingCount.Should().Be(3);
        }, DefaultTimeout);

        await queues.Enqueue(new FlowResume(f.Id) { MustRestart = true }, cancellationToken);

        await ComputedTest.When(async ct => {
            var flow = await GetFlow<TimerFlow>(flows, f.Id, ct);
            flow!.RemainingCount.Should().BeGreaterThan(3);
        }, DefaultTimeout);

        await WhenCompleted(flows, f.Id);
    }

    // Private methods

    private async Task<TFlow> GetLocalFlow<TFlow>(IFlows flows, Func<int, string> argumentFactory, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        FlowId flowId;
        Computed<IFlowData?> cFlowData;
        for (var i = 0;; i++) {
            flowId = flows.NewId<TimerFlow>(argumentFactory.Invoke(i));
            cFlowData = await Computed.Capture(() => flows.TryGetData(flowId, cancellationToken), cancellationToken);
            cFlowData.Value.Should().BeNull();
            if (cFlowData is not IRemoteComputed)
                break; // We need a remote flow
        }
        var flow = await flows.Get<TFlow>(flowId.Arguments, cancellationToken); // Starts the flow
        cFlowData.IsConsistent().Should().BeFalse();
        return flow;
    }

    private async Task<TFlow> GetRemoteFlow<TFlow>(IFlows flows, Func<int, string> argumentFactory, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        FlowId flowId;
        Computed<IFlowData?> cFlowData;
        for (var i = 0;; i++) {
            flowId = flows.NewId<TimerFlow>(argumentFactory.Invoke(i));
            cFlowData = await Computed.Capture(() => flows.TryGetData(flowId, cancellationToken), cancellationToken);
            cFlowData.Value.Should().BeNull();
            if (cFlowData is IRemoteComputed)
                break; // We need a remote flow
        }
        var flow = await flows.Get<TFlow>(flowId.Arguments, cancellationToken); // Starts the flow
        cFlowData.IsConsistent().Should().BeFalse();
        return flow;
    }

    private async Task<TFlow?> GetFlow<TFlow>(
        IFlows flows, FlowId flowId, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        var cFlowData = await GetFlowDataComputed(flows, flowId, cancellationToken).ConfigureAwait(false);
        var flowData = await cFlowData.Use(allowInconsistent: true, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flowData?.Flow;
    }

    private async Task<Computed<IFlowData?>> GetFlowDataComputed(
        IFlows flows, FlowId flowId, CancellationToken cancellationToken)
    {
        var cFlowData =  await Computed
            .Capture(() => flows.TryGetData(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        WriteLine($"[*] {cFlowData.Value?.Flow.ToString() ?? "null"} <- {cFlowData}");
        return cFlowData;
    }

    private Task WhenCompleted(IFlows flows, FlowId flowId)
        => ComputedTest.When(async ct => {
            var c = await GetFlowDataComputed(flows, flowId, ct);
            _ = c.UseUntyped(allowInconsistent: true, ct);
            var flow = c.Value?.Flow;
            flow.Require();
            flow.UntypedResult.Should().NotBeNull();
        }, DefaultTimeout);
}
