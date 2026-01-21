namespace ActualChat.Core.UnitTests.Channels;

public class ChannelExtTest
{
    [Fact]
    public async Task ProcessConcurrently_ShouldNotExceedConcurrencyLevel()
    {
        var channel = Channel.CreateUnbounded<int>();
        var maxConcurrency = 0;
        var concurrency = 0;

        for (var i = 0; i < 100; i++)
            await channel.Writer.WriteAsync(i);
        channel.Writer.TryComplete();

        await channel.ProcessConcurrently(4, async (_, ct) => {
            var c = Interlocked.Increment(ref concurrency);
            var observed = Volatile.Read(ref maxConcurrency);
            while (c > observed) {
                var prev = Interlocked.CompareExchange(ref maxConcurrency, c, observed);
                if (prev == observed)
                    break;
                observed = prev;
            }
            await Task.Delay(5, ct);
            Interlocked.Decrement(ref concurrency);
        });

        Assert.True(maxConcurrency <= 4, $"Expected maxConcurrency <= 4, but got {maxConcurrency}");
    }

    [Fact]
    public async Task ProcessConcurrently_ShouldSuppressItemExceptions()
    {
        var channel = Channel.CreateUnbounded<int>();
        for (var i = 0; i < 10; i++)
            await channel.Writer.WriteAsync(i);
        channel.Writer.TryComplete();

        var processed = 0;
        await channel.ProcessConcurrently(2, (i, _) => {
            Interlocked.Increment(ref processed);
            if (i % 2 == 0)
                throw new InvalidOperationException("Boom");
            return ValueTask.CompletedTask;
        });

        Assert.Equal(10, processed);
    }

    [Fact]
    public async Task Split_ShouldRouteItemsAndCompleteDestinations()
    {
        var source = Channel.CreateUnbounded<int>();
        var even = Channel.CreateUnbounded<int>();
        var odd = Channel.CreateUnbounded<int>();

        for (var i = 0; i < 20; i++)
            await source.Writer.WriteAsync(i);
        source.Writer.TryComplete();

        var pumpTask = source.Split([even, odd], i => i % 2);

        await pumpTask;
        even.Writer.TryComplete();
        odd.Writer.TryComplete();

        var evens = new List<int>();
        await foreach (var i in even.Reader.ReadAllAsync())
            evens.Add(i);
        var odds = new List<int>();
        await foreach (var i in odd.Reader.ReadAllAsync())
            odds.Add(i);

        Assert.All(evens, i => Assert.True(i % 2 == 0));
        Assert.All(odds, i => Assert.True(i % 2 == 1));
        Assert.Equal(10, evens.Count);
        Assert.Equal(10, odds.Count);
    }

    [Fact]
    public async Task Split_ShouldFailOnInvalidIndex()
    {
        var source = Channel.CreateUnbounded<int>();
        var d0 = Channel.CreateUnbounded<int>();

        await source.Writer.WriteAsync(1);
        source.Writer.TryComplete();

        var task = source.Split([d0], _ => 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => task);
    }
}
