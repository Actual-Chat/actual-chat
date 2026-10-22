using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class StreamSilenceWatchdogTest
{
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan LongHorizon = TimeSpan.FromSeconds(2);

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

    [Fact(Timeout = 5_000)]
    public async Task SteadyFrameFlow_NoCancel()
    {
        // arrange
        using var cts = new CancellationTokenSource();
        using var stopCts = new CancellationTokenSource();
        var tickCount = 0;
        var detectionCount = 0;

        // act
        // Every tick sees a frame. A producer thread feeding a counter made this test measure how a
        // loaded build agent schedules threads: one starved for ~300 ms looked like silence (#5564)
        var watchdog = StreamSilenceWatchdog.Run(
            consumeFrameCount: () => {
                Interlocked.Increment(ref tickCount);
                return 1;
            },
            onSilenceCts: cts,
            interval: ShortInterval,
            maxConsecutiveZeroIntervals: 3,
            onSilenceDetected: _ => Interlocked.Increment(ref detectionCount),
            cancellationToken: stopCts.Token);
        // Twice the zero-interval limit: a watchdog that ignored the frames would have fired by then
        await TestWait.WhenPolled(
            () => tickCount.Should().BeGreaterThanOrEqualTo(6),
            TimeSpan.FromSeconds(1));

        // assert
        cts.IsCancellationRequested.Should().BeFalse();
        detectionCount.Should().Be(0);

        // teardown
        await stopCts.CancelAsync();
        await watchdog;
    }

    [Fact(Timeout = 5_000)]
    public async Task ZeroFrames_CancelsAfterMaxConsecutiveIntervals()
    {
        // arrange
        using var cts = new CancellationTokenSource();
        using var stopCts = new CancellationTokenSource(LongHorizon);
        var detectionCount = 0;
        var detectedZeroCount = 0;

        // act
        var started = DateTime.UtcNow;
        await StreamSilenceWatchdog.Run(
            consumeFrameCount: () => 0,
            onSilenceCts: cts,
            interval: ShortInterval,
            maxConsecutiveZeroIntervals: 3,
            onSilenceDetected: zero => {
                Interlocked.Increment(ref detectionCount);
                detectedZeroCount = zero;
            },
            cancellationToken: stopCts.Token);
        var elapsed = DateTime.UtcNow - started;

        // assert
        cts.IsCancellationRequested.Should().BeTrue();
        detectionCount.Should().Be(1);
        detectedZeroCount.Should().Be(3);
        // 3 intervals × 20 ms = 60 ms minimum; no upper bound — CI timer slack
        // stretches 20 ms delays to hundreds of ms, and the 2 s stopCts horizon
        // already fails the asserts above if the watchdog doesn't finish in time.
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));
    }

    [Fact(Timeout = 5_000)]
    public async Task IntermittentFrame_ResetsCounter_NoCancel()
    {
        // arrange
        using var cts = new CancellationTokenSource();
        using var stopCts = new CancellationTokenSource();
        var pulses = 0;
        // We want the SECOND interval to see frames so the counter resets,
        // then the rest stay zero — but never long enough to fire (we cap
        // the run before it can hit 2 consecutive zeros).
        Func<int> consume = () => {
            // Pulse a frame on every other call so the counter alternates
            // 0, 1, 0, 1, ... — this resets `consecutiveZero` each pulse.
            return Interlocked.Increment(ref pulses) % 2 == 0 ? 1 : 0;
        };

        // act
        var watchdog = StreamSilenceWatchdog.Run(
            consumeFrameCount: consume,
            onSilenceCts: cts,
            interval: ShortInterval,
            maxConsecutiveZeroIntervals: 2,
            onSilenceDetected: null,
            cancellationToken: stopCts.Token);

        // Run for ~10 intervals. Pattern is 0,1,0,1,... so consecutiveZero
        // never reaches 2.
        await Task.Delay(ShortInterval * 10);

        // assert
        cts.IsCancellationRequested.Should().BeFalse();

        // teardown
        await stopCts.CancelAsync();
        await watchdog;
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
