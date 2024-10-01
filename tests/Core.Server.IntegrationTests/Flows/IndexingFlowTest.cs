using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public class IndexingFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(IndexingFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<IndexingFlowState>();
            services.AddSingleton<ChatEntryIndexingFlow>();
        },
    }, @out)
{
    [Fact]
    public async Task IndexingFlowStateMustBeAbleToStart()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();

        var f0 = await flows.GetOrStart<IndexingFlowState>("f0:3");
        f0.Should().NotBeNull();

        var f1 = await flows.GetOrStart<IndexingFlowState>("f1:2");
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
            flow.Should().NotBeNull();
            flow!.Step.Should().Be(FlowSteps.OnEnd);
        }, TimeSpan.FromSeconds(30));
}
