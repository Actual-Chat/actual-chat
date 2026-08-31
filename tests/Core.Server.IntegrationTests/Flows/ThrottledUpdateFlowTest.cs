using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ThrottledUpdateFlowCollection))]
[Trait("Category", "Slow")]
public sealed class ThrottledUpdateFlowTest(ThrottledUpdateFlowFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ThrottledUpdateFlowFixture>(fixture, @out)
{
    private static readonly TimeSpan ThrottlePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ScheduledUpdateShouldRunFlow()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // act
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // assert
        scheduled.Should().BeTrue("the flow doesn't exist yet");
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull("the scheduled update must create the flow");
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);
    }

    [Fact]
    public async Task SecondUpdateShouldBeThrottled()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        await FlowHub.TryScheduleUpdate<LongThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<LongThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);

        // act
        var scheduled = await FlowHub.TryScheduleUpdate<LongThrottledUpdateFlow>(target);

        // assert
        scheduled.Should().BeFalse("the throttle period hasn't elapsed");
        var flowAfter = await FlowHub.TryGet<LongThrottledUpdateFlow>(args);
        flowAfter.Should().NotBeNull("the flow ran at least once");
        flowAfter!.NextRunAt.Should().BeGreaterThan(FlowHub.SystemNow, "the next run is throttled until later");
    }

    [Fact]
    public async Task MustUpdateShouldBeFalseWithinThrottlePeriod()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // act
        var mustUpdateBeforeRun = await FlowHub.MustUpdate<LongThrottledUpdateFlow>(target);
        await FlowHub.TryScheduleUpdate<LongThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<LongThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);
        var mustUpdateAfterRun = await FlowHub.MustUpdate<LongThrottledUpdateFlow>(target);

        // assert
        mustUpdateBeforeRun.Should().BeTrue("the target was never updated");
        mustUpdateAfterRun.Should().BeFalse("the throttle period hasn't elapsed");
    }

    [Fact]
    public async Task ThrottledResumeShouldBeIgnored()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        await FlowHub.TryScheduleUpdate<LongThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<LongThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);

        // act
        // A directly scheduled resume event bypasses the TryScheduleUpdate throttle check
        await FlowHub.NewResumeEvent<LongThrottledUpdateFlow>(args).Schedule();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // assert
        var flow = await FlowHub.TryGet<LongThrottledUpdateFlow>(args);
        flow!.SuccessCount.Should().Be(1, "the throttled resume must be ignored");
        flow.Console.ToString().Should().NotContain("Run() #2", "the throttled resume must not run");
    }

    [Fact]
    public async Task FlowShouldRunAfterThrottlePeriod()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);

        // act
        await Task.Delay(ThrottlePeriod + TimeSpan.FromMilliseconds(500));
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // assert
        scheduled.Should().BeTrue("the throttle period has elapsed");
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #2 completed", "the second update must run");
        }, DefaultTimeout);
    }

    [Fact]
    public async Task TargetShouldSurviveArgumentsEncoding()
    {
        // arrange
        var target = $"https://example.com/page?q=test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);

        // act
        await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // assert
        await ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull("the scheduled update must create the flow");
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
            flow.Console.ToString().Should().Contain(target, "Target must decode back to the original string");
        }, DefaultTimeout);
    }

    [Fact]
    public void GetArgumentsShouldEncodeTarget()
    {
        // arrange
        var target = "https://example.com/test?foo=bar&baz=qux";

        // act
        var args = ThrottledUpdateFlow.GetArguments(target);

        // assert
        args.Should().NotBe(target, "the target must be encoded");
        args.FromBase64().Should().Be(target, "the encoding must be reversible");
    }
}

[CollectionDefinition(nameof(ThrottledUpdateFlowCollection))]
public sealed class ThrottledUpdateFlowCollection : ICollectionFixture<ThrottledUpdateFlowFixture>;

public sealed class ThrottledUpdateFlowFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(
    "throttled-update-flow",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => {
            services.AddFlows()
                .Add<SimpleThrottledUpdateFlow>()
                .Add<LongThrottledUpdateFlow>();
        },
    });
