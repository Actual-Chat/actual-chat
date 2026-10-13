using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Runs on its own, after the parallel collections of the assembly: their app hosts kept the runner's
// CPUs busy enough to push resumes staged 1s ahead to 5s

[CollectionDefinition(nameof(ResumeLatencyFlowCollection), DisableParallelization = true)]
public sealed class ResumeLatencyFlowCollection : ICollectionFixture<ResumeLatencyFlowFixture>;

public sealed class ResumeLatencyFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "resume-latency",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            // Master flows (migrations, indexing, sweeps) start with the host and load its DB
            // for ~15s - right when the resumes are measured (#4914)
            services.AddFlows(useMasterFlows: false).Add<ResumeLatencyFlow>();
        },
    });

[Collection(nameof(ResumeLatencyFlowCollection))]
public sealed class ResumeLatencyFlowTest(ResumeLatencyFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ResumeLatencyFlowFixture>(fixture, @out)
{
    // Resumes are staged 1s ahead: one miss is a busy runner, several are a regression
    private static readonly TimeSpan MaxTypicalDelay = TimeSpan.FromSeconds(3);
    // A loaded build agent misses two of five budgets in about a quarter of runs, but never three
    private static readonly int MaxMissedBudgets = TestRunnerInfo.IsBuildAgent() ? 2 : 1;
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
        var delaysText = delays.Select(x => x.ToShortString()).ToDelimitedString();
        delays.Max().Should().BeLessThan(StallDelay, $"no resume may stall (delays: {delaysText})");
    }

    [Fact]
    [Trait("Category", "Nightly")]
    public async Task ResumeDelaysShouldStayWithinBudget()
    {
        // Other test assemblies still load the runner, so the budget is checked nightly, not on every PR (#5297)

        // act
        var delays = await RunFlow();

        // assert
        var delaysText = delays.Select(x => x.ToShortString()).ToDelimitedString();
        delays.Count(x => x >= MaxTypicalDelay).Should().BeLessThanOrEqualTo(MaxMissedBudgets,
            $"at most {MaxMissedBudgets} resumes may miss their budget when DelayQuanta=0 (delays: {delaysText})");
        delays.Max().Should().BeLessThan(StallDelay, $"no resume may stall (delays: {delaysText})");
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
