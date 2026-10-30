using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class StreamSilenceWatchdogTest
{
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(20);

    [Fact(Timeout = 5_000)]
    public async Task ConstructorRejectsBadIntervals()
    {
        // arrange + act + assert
        await FluentActions.Awaiting(() => StreamSilenceWatchdog.Run(
            consumeFrameCount: () => 0,
            onSilenceCts: new CancellationTokenSource(),
            interval: TimeSpan.Zero,
            maxConsecutiveZeroIntervals: 2,
            onSilenceDetected: null,
            cancellationToken: default))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => StreamSilenceWatchdog.Run(
            consumeFrameCount: () => 0,
            onSilenceCts: new CancellationTokenSource(),
            interval: ShortInterval,
            maxConsecutiveZeroIntervals: 0,
            onSilenceDetected: null,
            cancellationToken: default))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SteadyFrameFlowShouldNeverBeSilent()
    {
        // arrange
        var detector = new StreamSilenceWatchdog.Detector(3);

        // act
        var verdicts = Enumerable.Range(0, 10).Select(_ => detector.Observe(1)).ToList();

        // assert
        verdicts.Should().AllBeEquivalentTo(false);
        detector.ConsecutiveZeroIntervals.Should().Be(0);
    }

    [Fact]
    public void ZeroFramesShouldBeSilentAfterMaxConsecutiveIntervals()
    {
        // arrange
        var detector = new StreamSilenceWatchdog.Detector(3);

        // act
        var verdicts = Enumerable.Range(0, 3).Select(_ => detector.Observe(0)).ToList();

        // assert
        verdicts.Should().Equal(false, false, true);
        detector.ConsecutiveZeroIntervals.Should().Be(3);
    }

    [Fact]
    public void IntermittentFrameShouldResetTheCount()
    {
        // arrange
        var detector = new StreamSilenceWatchdog.Detector(2);

        // act
        var verdicts = new[] { 0, 1, 0, 1, 0, 1, 0 }.Select(detector.Observe).ToList();

        // assert
        verdicts.Should().AllBeEquivalentTo(false, "a frame between two empty intervals breaks the run");
        detector.ConsecutiveZeroIntervals.Should().Be(1);
    }

    [Fact(Timeout = 5_000)]
    public async Task SilenceShouldCancelAndReportIt()
    {
        // arrange: a single empty interval is silence, so one tick is enough however slow the machine is
        using var cts = new CancellationTokenSource();
        var detectedZeroCounts = new List<int>();

        // act
        await StreamSilenceWatchdog.Run(
            consumeFrameCount: () => 0,
            onSilenceCts: cts,
            interval: ShortInterval,
            maxConsecutiveZeroIntervals: 1,
            onSilenceDetected: detectedZeroCounts.Add,
            cancellationToken: default);

        // assert
        cts.IsCancellationRequested.Should().BeTrue();
        detectedZeroCounts.Should().Equal(1);
    }

    [Fact(Timeout = 5_000)]
    public async Task ParentCancellation_ExitsCleanly_NoSilenceFire()
    {
        // arrange
        using var cts = new CancellationTokenSource();
        using var stopCts = new CancellationTokenSource();
        var detectionCount = 0;

        // act: cancel the parent before the watchdog can hit its silence threshold.
        var watchdog = StreamSilenceWatchdog.Run(
            consumeFrameCount: () => 0,
            onSilenceCts: cts,
            interval: TimeSpan.FromSeconds(5),  // long; only stopCts gets us out
            maxConsecutiveZeroIntervals: 2,
            onSilenceDetected: _ => Interlocked.Increment(ref detectionCount),
            cancellationToken: stopCts.Token);

        await Task.Delay(50);
        await stopCts.CancelAsync();
        await watchdog;

        // assert
        cts.IsCancellationRequested.Should().BeFalse();
        detectionCount.Should().Be(0);
    }
}
