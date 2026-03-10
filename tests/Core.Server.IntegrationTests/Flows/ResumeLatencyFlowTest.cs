using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[CollectionDefinition(nameof(ResumeLatencyFlowCollection))]
public class ResumeLatencyFlowCollection : ICollectionFixture<ResumeLatencyFlowFixture>;

public class ResumeLatencyFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "resume-latency",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<ResumeLatencyFlow>();
        },
    });

[Collection(nameof(ResumeLatencyFlowCollection))]
[Trait("Category", "Slow")]
public class ResumeLatencyFlowTest(ResumeLatencyFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ResumeLatencyFlowFixture>(fixture, @out)
{
    [Fact]
    public async Task ResumeDelayDoesNotExceed3Seconds()
    {
        var args = $"test-{Guid.NewGuid():N}";
        await FlowHub.NewResumeEvent<ResumeLatencyFlow>(args).Schedule();

        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<ResumeLatencyFlow>(args, ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull("flow should complete all 5 resumes");
        }, TimeSpan.FromSeconds(30));

        var completedFlow = await FlowHub.Get<ResumeLatencyFlow>(args);
        WriteLine(completedFlow.Console.ToString());
        completedFlow.MaxDelay.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "each resume should complete within 3 seconds when DelayQuanta=0");
    }
}
