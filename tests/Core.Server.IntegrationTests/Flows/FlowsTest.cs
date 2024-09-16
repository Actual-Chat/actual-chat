using ActualChat.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public class FlowsTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<TimerFlow>();
            services.AddHostedService<HostDisposeTrackerTester>();
        },
    }, @out)
{
    [Fact]
    public async Task TimerFlowTest()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.GetOrStart<TimerFlow>("f0:3");
        f0.Should().NotBeNull();

        var f1 = await flows.GetOrStart<TimerFlow>("f1:2");
        f1.Should().NotBeNull();

        await Task.WhenAll(
            WhenEnded(flows, f0.Id),
            WhenEnded(flows, f1.Id));

        Out.WriteLine("Test ended");
    }

    // Private methods

    private Task WhenEnded(IFlows flows, FlowId flowId)
        => ComputedTest.When(async ct => {
            var flow = await flows.Get(flowId, ct);
            Out.WriteLine($"[*] {flow?.ToString() ?? "null"}");
            flow.Should().BeNull();
        }, TimeSpan.FromSeconds(30));

    // Nested types

    public class HostDisposeTrackerTester(IServiceProvider services)
        : WorkerBase(services.HostDisposeTracker().NewCancellationTokenSource())
    {
        private IServiceProvider Services { get; } = services;

        protected override async Task OnRun(CancellationToken cancellationToken)
        {
            var @out = Services.GetRequiredService<ITestOutputHelper>();
            @out.WriteLine("Started.");
            await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(cancellationToken).SilentAwait();
            @out.WriteLine("Stopping.");
        }
    }
}
