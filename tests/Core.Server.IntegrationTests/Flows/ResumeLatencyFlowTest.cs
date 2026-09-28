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
public sealed class ResumeLatencyFlowTest(ResumeLatencyFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ResumeLatencyFlowFixture>(fixture, @out)
{
    // Resumes are staged 1s ahead: one miss is a busy runner, several are a regression
    private static readonly TimeSpan MaxTypicalDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StallDelay = TimeSpan.FromSeconds(10);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Resumes staged before the host owns its queue and flow shards wait for an owner,
        // and that wait lands in the measured delays
        await AppHost.Services.WhenFlowsStarted();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ResumesShouldNotStall()
    {
        // act
        var delays = await RunFlow();

        // assert
        delays.Max().Should().BeLessThan(StallDelay, "no resume may stall");
    }

    // Build agents lose up to ~2s per resume (#4647), so the budget is checked nightly, not on every PR
    [Fact]
    [Trait("Category", "Nightly")]
    public async Task ResumeDelaysShouldStayWithinBudget()
    {
        // act
        var delays = await RunFlow();

        // assert
        delays.Count(x => x >= MaxTypicalDelay).Should().BeLessThanOrEqualTo(1,
            "at most one resume may miss its budget when DelayQuanta=0");
        delays.Max().Should().BeLessThan(StallDelay, "no resume may stall");
    }

    private async Task<TimeSpan[]> RunFlow()
    {
        var args = $"test-{RandomStringGenerator.Default.Next()}";
        await FlowHub.NewResumeEvent<ResumeLatencyFlow>(args).Schedule();
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<ResumeLatencyFlow>(args, ct);
            flow.Should().NotBeNull("the scheduled resume must create the flow");
            flow.UntypedResult.Should().NotBeNull("the flow must complete all 5 resumes");
        }, TimeSpan.FromSeconds(30));

        var completedFlow = await FlowHub.Get<ResumeLatencyFlow>(args);
        WriteLine(completedFlow.Console.ToString());
        return completedFlow.Delays;
    }
}
