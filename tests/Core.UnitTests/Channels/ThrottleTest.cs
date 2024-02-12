namespace ActualChat.Core.UnitTests.Channels;

public class ThrottleTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "fix it later")]
    public async Task BasicTest()
    {
        // Normal sequence
        var delays = Delays(new [] {0.1, 0.1, 0.1, 0.1, 0.1});
        var l = await delays.Throttle(TimeSpan.FromSeconds(0.001)).ToArrayAsync();
        Out.WriteLine(l.ToDelimitedString());
        l.Length.Should().Be(6);
        l.Last().Should().Be(5);

        l = await delays.Throttle(TimeSpan.FromSeconds(0.25)).ToArrayAsync();
        Out.WriteLine(l.ToDelimitedString());
        l.Length.Should().BeLessThan(6);
        l.Last().Should().Be(5);

        l = await delays.Throttle(TimeSpan.FromSeconds(0.6)).ToArrayAsync();
        Out.WriteLine(l.ToDelimitedString());
        l.Length.Should().BeLessThan(3);
        l.Last().Should().Be(5);

        // Sequence ending w/ exception
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var seq = AppendThrow(delays, new InvalidOperationException());
            await seq.Throttle(TimeSpan.FromSeconds(0.25)).ToArrayAsync();
        });

        // Instant throw case
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var seq = AppendThrow(
                AsyncEnumerable.Empty<int>(),
                new InvalidOperationException());
            await seq.Throttle(TimeSpan.FromSeconds(0.25)).ToArrayAsync();
        });
    }

    public static async IAsyncEnumerable<int> Delays(
        IEnumerable<double> delays,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var i = 0;
        foreach (var delay in delays) {
            yield return i++;
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
        }
        yield return i;
    }

    public static async IAsyncEnumerable<T> AppendThrow<T>(
        IAsyncEnumerable<T> source,
        Exception error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;
        throw error;
    }
}
