using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(FailingThrottledUpdateFlowCollection))]
[Trait("Category", "Slow")]
public sealed class FailingThrottledUpdateFlowTest(FailingThrottledUpdateFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<FailingThrottledUpdateFlowFixture>(fixture, @out)
{
    // Three Run attempts are three commit + queue round trips; on CI one took up to 10 s, and
    // RetryAndRecoverTest used 25 s of a 30 s budget on a green run
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(20);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // The first test on this class's own host would otherwise spend its budget on the host's startup
        await AppHost.Services.WhenFlowsStarted();
        FailingThrottledUpdateFlow.CallCounts.Clear();
        FailingThrottledUpdateFlow.FailUntilCallCount = 0;
    }

    [Fact]
    public async Task RetryAndRecoverTest()
    {
        // arrange - fail first 2 calls, succeed on 3rd
        FailingThrottledUpdateFlow.FailUntilCallCount = 2;
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // act
        await FlowHub.TryScheduleUpdate<FailingThrottledUpdateFlow>(target);

        // assert - should eventually succeed after retries
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<FailingThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.SuccessCount.Should().Be(1);
            flow.FailCount.Should().Be(0);
        }, WaitBudget);

        // Verify Run was called 3 times
        FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target).Should().Be(3);
    }

    [Fact]
    public async Task GiveUpAfterMaxFailCountTest()
    {
        // arrange - always fail
        FailingThrottledUpdateFlow.FailUntilCallCount = int.MaxValue;
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        var scheduledAt = FlowHub.SystemNow;

        // act
        await FlowHub.TryScheduleUpdate<FailingThrottledUpdateFlow>(target);

        // assert - gives up after MaxFailCount (3) and throttles as after a success. NextRunAt is
        // checked against the scheduling time: against the current one it holds for only 2 s after
        // giving up, and a wait that first sees the flow later can never pass.
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<FailingThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.SuccessCount.Should().Be(0);
            flow.FailCount.Should().Be(0, "it is reset after giving up");
            flow.NextRunAt.Should().BeGreaterThanOrEqualTo(scheduledAt + FailingThrottledUpdateFlow.Throttle);
            flow.Console.ToString().Should().Contain("giving up");
        }, WaitBudget);
        FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target)
            .Should().Be(3, "Run is called MaxFailCount times");
    }

    [Fact]
    public async Task FailCountResetsOnSuccessTest()
    {
        // arrange - fail first call, succeed on 2nd
        FailingThrottledUpdateFlow.FailUntilCallCount = 1;
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // act
        await FlowHub.TryScheduleUpdate<FailingThrottledUpdateFlow>(target);

        // assert - should succeed after 1 retry, FailCount reset to 0
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<FailingThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.SuccessCount.Should().Be(1);
            flow.FailCount.Should().Be(0);
            flow.Console.ToString().Should().Contain("attempt 1/3");
            flow.Console.ToString().Should().Contain("Run() #1 completed");
        }, WaitBudget);
    }
}

[CollectionDefinition(nameof(FailingThrottledUpdateFlowCollection))]
public sealed class FailingThrottledUpdateFlowCollection : ICollectionFixture<FailingThrottledUpdateFlowFixture>;

public sealed class FailingThrottledUpdateFlowFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture(
        "failing-throttled-update-flow",
        messageSink,
        TestAppHostOptions.Default with {
            ConfigureServices = (_, services) => {
                services.AddFlows().Add<FailingThrottledUpdateFlow>();
            },
        });
