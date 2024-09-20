using ActualChat.IO;

namespace ActualChat.Core.UnitTests;

public class CancellableDebouncerTest
{
    [Fact]
    public async Task ShouldRunOnlyOnce()
    {
        // arrange
        var count = 0;
        var interval = TimeSpan.FromMilliseconds(1);
        await using var sut = new CancellableDebouncer(interval,
            _ => {
                Interlocked.Increment(ref count);
                return Task.CompletedTask;
            });

        // act
        sut.Enqueue();

        // assert
        await TestExt.When(async () => {
                count.Should().Be(1);
                await Task.Delay(interval + TimeSpan.FromMilliseconds(10));
                count.Should().Be(1);
            },
            TimeSpan.FromSeconds(3));
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
        var interval = TimeSpan.FromMilliseconds(100);
        await using var sut = new CancellableDebouncer<int>(interval,
            (i, _) => {
                Interlocked.Exchange(ref last, i);
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        // act
        for (int i = 1; i <= count; i++)
            sut.Enqueue(i);

        // assert
        await TestExt.When(async () => {
                callCount.Should().Be(1);
                last.Should().Be(count);
                await Task.Delay(interval + TimeSpan.FromMilliseconds(10));
                callCount.Should().Be(1);
                last.Should().Be(count);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(50)]
    public async Task ShouldRunManyTimesForRareCalls(int count)
    {
        // arrange
        var last = 0;
        var callCount = 0;
        var interval = TimeSpan.FromMilliseconds(1);
        var startedEvent = StateFactory.Default.NewMutable<int>();
        await using var sut = new CancellableDebouncer<int>(interval,
            (i, _) => {
                startedEvent.Value = i;
                Interlocked.Exchange(ref last, i);
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        // act
        for (int i = 1; i <= count; i++) {
            var prev = i - 1;
            await startedEvent.When(x => x == prev).WaitAsync(interval + TimeSpan.FromSeconds(1));
            sut.Enqueue(i);
        }

        // assert
        await TestExt.When(async () => {
                callCount.Should().Be(count);
                last.Should().Be(count);
                await Task.Delay(interval + TimeSpan.FromMilliseconds(10));
                callCount.Should().Be(count);
                last.Should().Be(count);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public async Task ShouldCancelPreviousAlreadyRunning(int count)
    {
        // arrange
        var startedCount = 0;
        var completedCount = 0;
        var interval = TimeSpan.FromMilliseconds(100);
        var startedEvent = StateFactory.Default.NewMutable<int>();
        await using var sut = new CancellableDebouncer<int>(interval,
            async (i, ct) => {
                Interlocked.Increment(ref startedCount);
                startedEvent.Value = i;
                await Task.Delay(1000, ct);
                Interlocked.Increment(ref completedCount);
            });

        // act
        for (int i = 1; i <= count; i++) {
            sut.Enqueue(i);
            var i1 = i;
            await startedEvent.When(x => x == i1).WaitAsync(TimeSpan.FromSeconds(3));
        }

        // assert
        await TestExt.When(async () => {
                startedCount.Should().Be(count);
                completedCount.Should().Be(1);
                await Task.Delay(interval + TimeSpan.FromMilliseconds(10));
                startedCount.Should().Be(count);
                completedCount.Should().Be(1);
            },
            TimeSpan.FromSeconds(3));
    }
}
