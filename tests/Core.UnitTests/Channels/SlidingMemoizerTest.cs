namespace ActualChat.Core.UnitTests.Channels;

public class SlidingMemoizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task BasicReplay()
    {
        var items = Enumerable.Range(0, 5).ToArray();
        var memoizer = items.ToAsyncEnumerable().SlidingMemoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Should().BeEquivalentTo(items, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SlidingWindowTrims()
    {
        var items = Enumerable.Range(0, 20).ToArray();
        var memoizer = items.ToAsyncEnumerable().SlidingMemoize(8);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Count.Should().Be(8);
        // Should contain the last 8 items
        replayed.Should().BeEquivalentTo(Enumerable.Range(12, 8), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ExactCapacity()
    {
        var items = Enumerable.Range(0, 10).ToArray();
        var memoizer = items.ToAsyncEnumerable().SlidingMemoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Should().BeEquivalentTo(items, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task MultipleConsumers()
    {
        var channel = Channel.CreateUnbounded<int>();
        var memoizer = channel.Reader.ReadAllAsync().SlidingMemoize(100);

        for (var i = 0; i < 5; i++)
            await channel.Writer.WriteAsync(i);

        await Task.Delay(50);

        using var cts = new CancellationTokenSource();
        var consumer1 = memoizer.Replay(cts.Token).ToListAsync(cts.Token);
        var consumer2 = memoizer.Replay(cts.Token).ToListAsync(cts.Token);

        for (var i = 5; i < 8; i++)
            await channel.Writer.WriteAsync(i);

        channel.Writer.Complete();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var list1 = await consumer1.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var list2 = await consumer2.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        list1.Count.Should().Be(8);
        list2.Count.Should().Be(8);
        list1.Should().BeEquivalentTo(list2, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task LateJoinerGetsOnlyBuffer()
    {
        var items = Enumerable.Range(0, 100).ToArray();
        var memoizer = items.ToAsyncEnumerable().SlidingMemoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Count.Should().Be(10);
        replayed.Should().BeEquivalentTo(Enumerable.Range(90, 10), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task LiveConsumerGetsAllItems()
    {
        var channel = Channel.CreateUnbounded<int>();
        var memoizer = channel.Reader.ReadAllAsync().SlidingMemoize(100);

        using var cts = new CancellationTokenSource();
        var allItems = new List<int>();
        var consumerTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay(cts.Token))
                allItems.Add(item);
        });

        await Task.Delay(50);

        for (var i = 0; i < 10; i++)
            await channel.Writer.WriteAsync(i);

        channel.Writer.Complete();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Live consumer receives ALL items, not just last 4
        allItems.Count.Should().Be(10);
        allItems.Should().BeEquivalentTo(Enumerable.Range(0, 10), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task CompletionPropagates()
    {
        var channel = Channel.CreateUnbounded<int>();
        var memoizer = channel.Reader.ReadAllAsync().SlidingMemoize(10);

        await channel.Writer.WriteAsync(1);
        await Task.Delay(50);

        using var cts = new CancellationTokenSource();
        var replayTask = memoizer.Replay(cts.Token).ToListAsync(cts.Token);

        channel.Writer.Complete();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await replayTask.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeEquivalentTo(new[] { 1 });
    }

    [Fact]
    public async Task EmptySource()
    {
        var memoizer = AsyncEnumerable.Empty<int>().SlidingMemoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationWorks()
    {
        var channel = Channel.CreateUnbounded<int>();
        var memoizer = channel.Reader.ReadAllAsync().SlidingMemoize(10);

        await channel.Writer.WriteAsync(1);
        await Task.Delay(50);

        using var cts = new CancellationTokenSource();
        var items = new List<int>();
        var replayTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay(cts.Token))
                items.Add(item);
        });

        await Task.Delay(100);
        items.Count.Should().Be(1);

        await cts.CancelAsync();

        var act = () => replayTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>();

        channel.Writer.Complete();
    }

    [Fact]
    public async Task CapacityOne()
    {
        var items = Enumerable.Range(0, 50).ToArray();
        var memoizer = items.ToAsyncEnumerable().SlidingMemoize(1);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay(CancellationToken.None).ToListAsync();
        replayed.Should().BeEquivalentTo(new[] { 49 });
    }

    [Fact]
    public async Task SlowConsumerDisconnected()
    {
        var channel = Channel.CreateUnbounded<int>();
        // Ring buffer = 100, but each consumer channel only holds 4
        var memoizer = channel.Reader.ReadAllAsync().SlidingMemoize(100, consumerCapacity: 4);

        // Start a "fast" consumer that reads everything
        var fastItems = new List<int>();
        var fastTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay(CancellationToken.None))
                fastItems.Add(item);
        });

        // Start a "slow" consumer that blocks after subscribing, never reads
        var slowReplay = memoizer.Replay(CancellationToken.None);
        var slowEnumerator = slowReplay.GetAsyncEnumerator();
        // Don't call MoveNextAsync — the consumer never reads

        await Task.Delay(50); // let fast consumer subscribe

        // Write enough items to overflow the slow consumer's bounded channel (capacity 4)
        for (var i = 0; i < 20; i++) {
            await channel.Writer.WriteAsync(i);
            await Task.Delay(10); // give fast consumer time to drain
        }

        channel.Writer.Complete();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        await fastTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Fast consumer got all items
        fastItems.Count.Should().Be(20);

        // Slow consumer's enumerator should end quickly since its channel was completed
        var slowItems = new List<int>();
        while (await slowEnumerator.MoveNextAsync())
            slowItems.Add(slowEnumerator.Current);

        // Slow consumer was disconnected — got at most consumerCapacity items (what fit in the bounded channel)
        slowItems.Count.Should().BeLessThanOrEqualTo(4);
        await slowEnumerator.DisposeAsync();
    }
}
