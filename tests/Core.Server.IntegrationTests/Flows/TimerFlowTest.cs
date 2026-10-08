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
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(15);
    // Above WaitBudget scaled, below the Timeout attribute, and not scaled itself - the
    // constant it has to stay under cannot scale either
    private static readonly TimeSpan CancelAfter = TimeSpan.FromSeconds(90);

    private TestAppHost H0 { get; set; } = null!;
    private TestAppHost H1 { get; set; } = null!;

    protected override async Task DisposeAsync()
    {
        if (H1 is not null)
            await H1.DisposeSilentlyAsync();
        if (H0 is not null)
            await H0.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task BasicTest()
    {
        using var cts = new CancellationTokenSource(CancelAfter);
        var cancellationToken = cts.Token;

        var flowHub = H0.Services.FlowHub();

        var flowDef = flowHub.Defs.Get<TimerFlow>();
        flowDef.DataVersion.Should().Be(2);
        flowDef.ResumeTimeout.Should().Be(TimeSpan.FromSeconds(60));

        var command = flowHub.NewResumeEvent<TimerFlow>("f0,1");
        var queueRef = QueueRef.For(command, H0.Services);
        queueRef.ShardScheme.Should().Be(ShardScheme.SlowQueue); // See [Flow] attribute on TimerFlow

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},2", cancellationToken);
        WriteLine($"f0.Id: {f.Id}");

        await WhenCompleted(flowHub, f.Id);
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoFlowsTest()
    {
        using var cts = new CancellationTokenSource(CancelAfter);
        var cancellationToken = cts.Token;

        var flowHub = H0.Services.FlowHub();

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},2", cancellationToken);
        f.Should().NotBeNull();
        var g = await GetLocalFlow<TimerFlow>(flowHub, i => $"g{i},2", cancellationToken);
        g.Should().NotBeNull();

        await Task.WhenAll(
            WhenCompleted(flowHub, f.Id),
            WhenCompleted(flowHub, g.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task ResetTest()
    {
        using var cts = new CancellationTokenSource(CancelAfter);
        var cancellationToken = cts.Token;

        var flowHub = H0.Services.FlowHub();
        var queues = H0.Services.Queues();

        var f = await GetRemoteFlow<TimerFlow>(flowHub, i => $"f{i},5", cancellationToken);
        f.Should().NotBeNull();

        // Each count lasts a second, so a loaded runner can step over any exact one - wait for a bound.
        // RemainingCount is 0 before Init too, so 0 counts only once the flow has completed.
        await TestWait.When(async ct => {
            var flow = await GetFlow<TimerFlow>(flowHub, f.Id, ct);
            var hasProgressed = flow!.RemainingCount is > 0 and <= 3 || flow.UntypedResult is not null;
            hasProgressed.Should().BeTrue(
                $"the flow must count down to 3 or below, but it's at {flow.RemainingCount}");
        }, WaitBudget);
        var initCount = TimerFlow.InitCounts.GetValueOrDefault(f.Id);

        await queues.Enqueue(flowHub.NewResumeEvent(f.Id).WithReset(), cancellationToken);

        await TestWait.WhenPolled(
            () => TimerFlow.InitCounts.GetValueOrDefault(f.Id).Should().BeGreaterThan(initCount,
                "the reset must re-run Init"),
            WaitBudget);

        await WhenCompleted(flowHub, f.Id);
    }

    // Protected/internal methods

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Keeps the hosts' startup out of the test's timeout: on a loaded CI runner H0's DB recreation
        // and migrations took 60-80 s, leaving the flow 10-20 s of CancelAfter.
        H0 = await NewAppHost();
        H1 = await NewAppHost(o => o with { MustInitializeDb = false });
        WriteLine($"h0.ThisNode: {H0.Services.MeshWatcher().ThisNode}");
        WriteLine($"h1.ThisNode: {H1.Services.MeshWatcher().ThisNode}");
        await H0.Services.WhenFlowsStarted();
        await H1.Services.WhenFlowsStarted();
    }

    // Private methods

    private async Task<TFlow> GetLocalFlow<TFlow>(
        FlowHub hub, Func<int, string> argumentFactory, CancellationToken cancellationToken)
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

    private async Task<TFlow> GetRemoteFlow<TFlow>(
        FlowHub hub, Func<int, string> argumentFactory, CancellationToken cancellationToken)
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
        => TestWait.When(async ct => {
            var c = await GetFlowDataComputed(hub, flowId, ct);
            _ = c.UseUntyped(allowInconsistent: true, ct);
            var flow = c.Value?.GetFlow(hub);
            flow.Require();
            flow.UntypedResult.Should().NotBeNull();
        }, WaitBudget);
}
