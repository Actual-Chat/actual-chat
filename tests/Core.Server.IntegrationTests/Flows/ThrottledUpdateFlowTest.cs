using ActualChat.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ThrottledUpdateFlowCollection))]
[Trait("Category", "Slow")]
public class ThrottledUpdateFlowTest(ThrottledUpdateFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ThrottledUpdateFlowFixture>(fixture, @out)
{
    private static readonly TimeSpan ThrottlePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task BasicTest()
    {
        // Arrange
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act - schedule first update
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // Assert - should schedule since flow doesn't exist yet
        scheduled.Should().BeTrue();

        // Wait for flow to run
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);
    }

    [Fact]
    public async Task ThrottlingTest()
    {
        // Arrange
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act - schedule first update
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // Wait for flow to run once
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);

        // Act - try to schedule immediately (should be throttled)
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // Assert - should not schedule since throttle period hasn't elapsed
        scheduled.Should().BeFalse();

        // Get flow and verify NextRunAt is in the future
        var flowAfter = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args);
        flowAfter.Should().NotBeNull();
        flowAfter!.NextRunAt.Should().BeGreaterThan(FlowHub.SystemNow);
    }

    [Fact]
    public async Task MustUpdateTest()
    {
        // Arrange
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act & Assert - new target should need update
        var mustUpdate1 = await FlowHub.MustUpdate<SimpleThrottledUpdateFlow>(target);
        mustUpdate1.Should().BeTrue();

        // Schedule and wait for completion
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);

        // Act & Assert - should not need update immediately after
        var mustUpdate2 = await FlowHub.MustUpdate<SimpleThrottledUpdateFlow>(target);
        mustUpdate2.Should().BeFalse();
    }

    [Fact]
    public async Task ThrottledResumeIsIgnoredTest()
    {
        // Arrange
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Schedule and wait for first run
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);

        // Force schedule a resume event (bypassing the TryScheduleUpdate check)
        await FlowHub.NewResumeEvent<SimpleThrottledUpdateFlow>(args).Schedule();

        // Give some time for the resume to be processed
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert - SuccessCount should still be 1 (throttled resume was ignored or not run)
        var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args);
        flow!.SuccessCount.Should().Be(1);
        flow.Console.ToString().Should().NotContain("Run() #2");
    }

    [Fact]
    public async Task RunsAfterThrottlePeriodTest()
    {
        // Arrange
        var target = $"test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Schedule and wait for first run
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
        }, DefaultTimeout);

        // Wait for throttle period to elapse (add extra buffer for timing)
        await Task.Delay(ThrottlePeriod + TimeSpan.FromMilliseconds(500));

        // Schedule another update - should be allowed now
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);
        scheduled.Should().BeTrue();

        // Wait for second run (increase timeout for this test)
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #2 completed");
        }, DefaultTimeout);
    }

    [Fact]
    public async Task TargetPropertyTest()
    {
        // Arrange - use unique target with special characters to test encoding
        var target = $"https://example.com/page?q=test-{Guid.NewGuid():N}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Act
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // Wait for flow to run and verify the target was logged
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull();
            flow!.Console.ToString().Should().Contain("Run() #1 completed");
            flow.Console.ToString().Should().Contain(target);
        }, DefaultTimeout);
    }

    [Fact]
    public void GetArgumentsEncodesTarget()
    {
        // Arrange
        var target = "https://example.com/test?foo=bar&baz=qux";

        // Act
        var args = ThrottledUpdateFlow.GetArguments(target);

        // Assert
        args.Should().NotBe(target);
        args.FromBase64().Should().Be(target);
    }
}

[CollectionDefinition(nameof(ThrottledUpdateFlowCollection))]
public class ThrottledUpdateFlowCollection : ICollectionFixture<ThrottledUpdateFlowFixture>;

public class ThrottledUpdateFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "throttled-update-flow",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows().Add<SimpleThrottledUpdateFlow>();
        },
    });
