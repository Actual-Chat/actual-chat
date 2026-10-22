namespace ActualChat.Core.UnitTests;

// The debouncer runs on a ManualClock, so an interval passes only when a test moves the clock - never
// because a loaded build agent held a thread for longer than the interval (#5089)
public sealed class CancellingDebouncerTest
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ShouldRunOnlyOnce()
    {
        // arrange
        var count = 0;
        var clock = new ManualClock();
        await using var sut = Debouncer.New<Unit>(clock, Interval,
            (_, _) => {
                Interlocked.Increment(ref count);
                return Task.CompletedTask;
            });

        // act
        sut.Enqueue(default);

        // assert
        await WhenIntervalsPass(clock, () => count.Should().Be(1));
        await ShouldStay(clock, () => count.Should().Be(1));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(7_890)]
    public async Task ShouldRunOnlyOnceForManyFrequentCalls(int count)
    {
        // arrange
        var last = 0;
        var callCount = 0;
        var clock = new ManualClock();
        await using var sut = Debouncer.New<int>(clock, Interval,
            (i, _) => {
                Interlocked.Exchange(ref last, i);
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        // act
        for (var i = 1; i <= count; i++)
            sut.Enqueue(i);

        // assert
        await WhenIntervalsPass(clock, () => {
            callCount.Should().Be(1);
            last.Should().Be(count);
        });
        await ShouldStay(clock, () => {
            callCount.Should().Be(1);
            last.Should().Be(count);
        });
    }

    [Theory]
    [InlineData(3)]
    [InlineData(50)]
    public async Task ShouldRunManyTimesForRareCalls(int count)
    {
        // arrange
        var last = 0;
        var callCount = 0;
        var clock = new ManualClock();
        await using var sut = Debouncer.New<int>(clock, Interval,
            (i, _) => {
                Interlocked.Exchange(ref last, i);
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        // act
        for (var i = 1; i <= count; i++) {
            sut.Enqueue(i);
            var expectedCallCount = i;
            await WhenIntervalsPass(clock, () => callCount.Should().Be(expectedCallCount));
        }

        // assert
        await ShouldStay(clock, () => {
            callCount.Should().Be(count);
            last.Should().Be(count);
        });
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public async Task ShouldCancelPreviousAlreadyRunning(int count)
    {
        // arrange
        var startedCount = 0;
        var completedCount = 0;
        var whenReleased = TaskCompletionSourceExt.New();
        var clock = new ManualClock();
        await using var sut = Debouncer.New<int>(clock, Interval,
            async (_, ct) => {
                Interlocked.Increment(ref startedCount);
                await whenReleased.Task.WaitAsync(ct);
                Interlocked.Increment(ref completedCount);
            });

        // act
        for (var i = 1; i <= count; i++) {
            sut.Enqueue(i);
            var expectedStartedCount = i;
            await WhenIntervalsPass(clock, () => startedCount.Should().Be(expectedStartedCount));
        }
        whenReleased.SetResult();

        // assert
        await TestWait.WhenPolled(() => completedCount.Should().Be(1));
        await ShouldStay(clock, () => {
            startedCount.Should().Be(count);
            completedCount.Should().Be(1);
        });
    }

    // Private methods

    private static Task WhenIntervalsPass(ManualClock clock, Action assertion)
        // The clock moves on every check rather than once: the debouncer reads the time before it starts
        // waiting, so a single move made in between would leave it waiting for the next one
        => TestWait.WhenPolled(() => {
            clock.Time += Interval;
            assertion.Invoke();
        });

    private static async Task ShouldStay(ManualClock clock, Action assertion)
    {
        clock.Time += Interval * 3;
        await Task.Delay(50);
        assertion.Invoke();
    }
}
