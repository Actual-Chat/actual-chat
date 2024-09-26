using ActualChat.Flows;
using ActualChat.Flows.Builder;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Notes:
// For this case we should have the following properties of the service:
// - It must react on triggered events.
// - It must supply a list of a low priority events or calculate it.
// - It must be able to start on it's own.
// - It must not fail in case of a high volume of incoming events.
// - It must be able to restart in case of a failure.
public class TriggeredFlowsTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<TriggeredFlow>();
            /*
            // What I would've expect here:
            services.AddFlows().Add(
                new Flow<FlowImplementation>(this as FlowImplementation)
                    // This must be a pull model
                    .For<ChatId>(e => e.ListChats).Do(e => e.IndexChat)
                    // An this is a push model
                    .On<SomeEvent>((FlowImplementation f, SomeEvent e) => e.HandleSomeEvent(e))
                    .On<SomeOtherEvent>(e => e.HandleSomeOtherEvent)
            );*/

            
            // What I want to be working
            //services.FindInstance<ChatIndexingFlow>();
            services.AddFlows().Add(
                new FlowBuilder()
                    // This must be a pull model
                    //.For<ChatId>(e => e.ListChats).Do(e => e.IndexChat)
                    // An this is a push model
                    .On<SomeEvent>(async (e, _c) => {
                        
                        return new FlowId("TriggeredFlow:OnReset:some-event");
                    })
            );

            services.AddSingleton<SomeEventSource>();
        },
    }, @out)
{
    private string ConvertIntoFlowId() {
        return null;
    }

    private static async Task HandleSomeEvent(SomeEvent e, CancellationToken cancellationToken){
        return;
    }

    [Fact]
    public async Task ItMustNotStartWithNoEventTriggered()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var f0 = await flows.Get<TriggeredFlow<SomeEvent>>("some-key:some-data");
        f0.Should().BeNull();
        Out.WriteLine("Test ended");
    }

    [Fact]
    public async Task ItMustStartOnEventTriggered()
    {
        using var h = await NewAppHost();
        var flows = h.Services.GetRequiredService<IFlows>();
        var someEventSource = h.Services.GetRequiredService<SomeEventSource>();
        await someEventSource.EnqueueEvent("some-key", "some-data");
        var f0 = await flows.Get<TriggeredFlow<SomeEvent>>("some-key:some-data");
        f0.Should().NotBeNull();
        await WhenEnded(flows, f0!.Id);
        Out.WriteLine("Test ended");
    }
    [Fact]
    public async Task MustStartFlowOnTrigger()
    {
    }

    [Fact]
    public async Task MustStartFlowOnAnyTriggerWhenMultipleTriggersAccepted()
    {
    }

    [Fact]
    public async Task MustExecuteFlowOnCorrectShardRegardlessOfTriggerSourceShard()
    {
    }

    [Fact]
    public async Task MustPushBackOnAnOverflowOfTriggeredEvents()
    {
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
