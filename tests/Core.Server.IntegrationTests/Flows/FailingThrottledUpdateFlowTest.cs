using ActualChat.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(FailingThrottledUpdateFlowCollection))]
[Trait("Category", "Slow")]
public class FailingThrottledUpdateFlowTest(FailingThrottledUpdateFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<FailingThrottledUpdateFlowFixture>(fixture, @out)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        FailingThrottledUpdateFlow.CallCounts.Clear();
        FailingThrottledUpdateFlow.FailUntilCallCount = 0;
    }

    [Fact]
    public async Task RetryAndRecoverTest()
    {
        // Arrange - fail first 2 calls, succeed on 3rd
        FailingThrottledUpdateFlow.FailUntilCallCount = 2;
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act
        await FlowHub.TryScheduleUpdate<FailingThrottledUpdateFlow>(target);

        // Assert - should eventually succeed after retries
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<FailingThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.SuccessCount.Should().Be(1);
            flow.FailCount.Should().Be(0);
        }, DefaultTimeout);

        // Verify Run was called 3 times
        FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target).Should().Be(3);
    }

    [Fact]
    public async Task GiveUpAfterMaxFailCountTest()
    {
        // arrange - always fail
        FailingThrottledUpdateFlow.FailUntilCallCount = int.MaxValue;
        var target = $"test-{Guid.NewGuid():N}";
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
        }, DefaultTimeout);
        FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target).Should().Be(3, "Run is called MaxFailCount times");
    }

    [Fact]
    public async Task FailCountResetsOnSuccessTest()
    {
        // Arrange - fail first call, succeed on 2nd
        FailingThrottledUpdateFlow.FailUntilCallCount = 1;
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act
        await FlowHub.TryScheduleUpdate<FailingThrottledUpdateFlow>(target);

        // Assert - should succeed after 1 retry, FailCount reset to 0
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<FailingThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.SuccessCount.Should().Be(1);
            flow.FailCount.Should().Be(0);
            flow.Console.ToString().Should().Contain("attempt 1/3");
            flow.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);
    }
}

[CollectionDefinition(nameof(FailingThrottledUpdateFlowCollection))]
public class FailingThrottledUpdateFlowCollection : ICollectionFixture<FailingThrottledUpdateFlowFixture>;

public class FailingThrottledUpdateFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "failing-throttled-update-flow",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<FailingThrottledUpdateFlow>();
        },
    });
