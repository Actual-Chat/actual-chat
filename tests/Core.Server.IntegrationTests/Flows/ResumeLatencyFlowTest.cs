using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[CollectionDefinition(nameof(ResumeLatencyFlowCollection))]
public sealed class ResumeLatencyFlowCollection : ICollectionFixture<ResumeLatencyFlowFixture>;

public sealed class ResumeLatencyFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "resume-latency",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<ResumeLatencyFlow>();
        },
    });

[Collection(nameof(ResumeLatencyFlowCollection))]
[Trait("Category", "Slow")]
public sealed class ResumeLatencyFlowTest(ResumeLatencyFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ResumeLatencyFlowFixture>(fixture, @out)
{
    // Resumes are staged 1s ahead: one miss is a busy runner, several are a regression
    private static readonly TimeSpan MaxTypicalDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StallDelay = TimeSpan.FromSeconds(10);
    [Fact]
    public async Task ResumeDelaysShouldStayWithinBudget()
    {
        // arrange
        var args = $"test-{RandomStringGenerator.Default.Next()}";

        // act
        await FlowHub.NewResumeEvent<ResumeLatencyFlow>(args).Schedule();
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<ResumeLatencyFlow>(args, ct);
            flow.Should().NotBeNull("the scheduled resume must create the flow");
            flow.UntypedResult.Should().NotBeNull("the flow must complete all 5 resumes");
        }, TimeSpan.FromSeconds(30));

        // assert
        var completedFlow = await FlowHub.Get<ResumeLatencyFlow>(args);
        WriteLine(completedFlow.Console.ToString());
        var delays = completedFlow.Delays;
        delays.Count(x => x >= MaxTypicalDelay).Should().BeLessThanOrEqualTo(1,
            "at most one resume may miss its budget when DelayQuanta=0");
        delays.Max().Should().BeLessThan(StallDelay, "no resume may stall");
    }
}
