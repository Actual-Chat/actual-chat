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
        await TestWait.When(async ct => {
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
        await TestWait.When(async ct => {
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
        await TestWait.When(async ct => {
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
        await TestWait.When(async ct => {
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
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #1 completed", "the scheduled update must run");
        }, DefaultTimeout);

        // act
        await Task.Delay(ThrottlePeriod + TimeSpan.FromMilliseconds(500));
        var scheduled = await FlowHub.TryScheduleUpdate<SimpleThrottledUpdateFlow>(target);

        // assert
        scheduled.Should().BeTrue("the throttle period has elapsed");
        await TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<SimpleThrottledUpdateFlow>(args, ct);
            flow!.Console.ToString().Should().Contain("Run() #2 completed", "the second update must run");
        }, DefaultTimeout);
    }

    [Fact]
    public async Task ScheduledUpdateShouldNotStoreABlankFlowAhead()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        using var gate = GatedThrottledUpdateFlow.Close(target);

        // act
        await FlowHub.TryScheduleUpdate<GatedThrottledUpdateFlow>(target);
        await WhenRunEntered(gate);
        var flowWhileRunning = await FlowHub.TryGet<GatedThrottledUpdateFlow>(args);
        gate.Open();

        // assert
        // A blank stored ahead of the first resume comes with a resume of its own - a second chain
        flowWhileRunning.Should().BeNull("the first resume stores the flow, nothing stores it before that");
        await WhenFirstRunCompleted(args);
    }

    [Fact]
    public async Task ResumeOfMissingFlowShouldNotStoreABlankFlowAhead()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        using var gate = GatedThrottledUpdateFlow.Close(target);

        // act
        await FlowHub.NewResumeEvent<GatedThrottledUpdateFlow>(args).Schedule();
        await WhenRunEntered(gate);
        var flowWhileRunning = await FlowHub.TryGet<GatedThrottledUpdateFlow>(args);
        gate.Open();

        // assert
        flowWhileRunning.Should().BeNull("the resume stores the flow on commit, nothing stores it before that");
        await WhenFirstRunCompleted(args);
    }

    [Fact]
    public async Task NewFlowShouldResumeOnlyWhenAsked()
    {
        // arrange
        var target = $"test-{RandomStringGenerator.Default.Next()}";
        var args = ThrottledUpdateFlow.GetArguments(target);
        await FlowHub.TryScheduleUpdate<GatedThrottledUpdateFlow>(target);
        await WhenFirstRunCompleted(args);

        // act
        var markerScheduledAt = FlowHub.SystemNow;
        await FlowHub.NewResumeEvent<GatedThrottledUpdateFlow>(args).Schedule();

        // assert
        // A second chain's resume is sent with the first commit, well ahead of the marker.
        // Polled: the resume times are a plain in-memory list, nothing invalidates on it
        await TestWait.WhenPolled(
            () => GatedThrottledUpdateFlow.GetResumeTimes(target)
                .Should().Contain(x => x >= markerScheduledAt, "the marker resume must be handled"),
            DefaultTimeout);

        GatedThrottledUpdateFlow.GetResumeTimes(target)
            .Should().HaveCount(2, "the first resume and the marker are the only ones asked for");
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
        await TestWait.When(async ct => {
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

    // Private methods

    private static Task WhenRunEntered(
        GatedThrottledUpdateFlow.Gate gate,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        // Polled: the gate is a plain task, nothing invalidates on it
        => TestWait.WhenPolled(
            () => gate.Entered.Task.IsCompleted.Should().BeTrue("the first resume must reach Run()"),
            DefaultTimeout, callerFilePath: callerFilePath, callerLine: callerLine);

    private Task WhenFirstRunCompleted(
        string args,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        => TestWait.When(async ct => {
            var flow = await FlowHub.TryGet<GatedThrottledUpdateFlow>(args, ct);
            flow.Should().NotBeNull("the first resume must store the flow");
            flow!.SuccessCount.Should().Be(1, "the first run must complete once the gate opens");
        }, DefaultTimeout, callerFilePath: callerFilePath, callerLine: callerLine);
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
                .Add<LongThrottledUpdateFlow>()
                .Add<GatedThrottledUpdateFlow>();
        },
    });
