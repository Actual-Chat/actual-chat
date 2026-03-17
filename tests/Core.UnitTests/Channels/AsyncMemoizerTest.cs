namespace ActualChat.Core.UnitTests.Channels;

public class AsyncMemoizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    // === Basic tests (bounded) ===

    [Fact]
    public async Task EmptyStream_Completes()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().BeEmpty();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeOfType<ChannelClosedException>();
    }

    [Fact]
    public async Task SingleItem()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(1);
    }

    [Fact]
    public async Task ItemsWithinCapacity()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task LateReplay_GetsOnlyBufferedTail()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 10; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(4);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(7, 8, 9, 10);
    }

    [Fact]
    public async Task TwoConsumers_BothGetItems_PreRegistered()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(16);

        var items1 = await memoizer.Replay().ToListAsync();
        var items2 = await memoizer.Replay().ToListAsync();

        items1.Should().Equal(1, 2, 3, 4, 5);
        items2.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task TailSize_Zero_NoHistory()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(16);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay(0).ToListAsync();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task TailSize_Partial()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 10; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(16);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay(3).ToListAsync();
        items.Should().Equal(8, 9, 10);
    }

    [Fact]
    public async Task TailSize_LargerThanAvailable()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 3; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay(100).ToListAsync();
        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ErrorCompletion()
    {
        var error = new InvalidOperationException("test error");
        var source = CreateFailingSource(new[] { 1, 2, 3 }, error);
        var memoizer = source.Memoize(8);

        var items = new List<int>();
        var caughtError = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });

        caughtError.Should().BeSameAs(error);
        items.Should().Equal(1, 2, 3);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => memoizer.ReadTask.WaitAsync(TimeSpan.FromSeconds(5)));

        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeSameAs(error);
    }

    [Fact]
    public async Task SuccessfulCompletion()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(1, 2);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeOfType<ChannelClosedException>();
    }

    [Fact]
    public async Task Dispose_ReturnsBuffer()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        await memoizer.Replay().ToListAsync();
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        memoizer.Dispose();
        memoizer.Dispose(); // double dispose safe
    }

    [Fact]
    public async Task Capacity_RingBufferEvictsOldItems()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 10; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(3);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(8, 9, 10);
    }

    [Fact]
    public async Task Memoize_FromAsyncEnumerable()
    {
        var source = new[] { 1, 2, 3 }.ToAsyncEnumerable();
        var memoizer = source.Memoize(8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task MultipleReplays_AfterCompletion()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(8);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        for (var r = 0; r < 3; r++) {
            var items = await memoizer.Replay().ToListAsync();
            items.Should().Equal(1, 2, 3, 4, 5);
        }
    }

    [Fact]
    public async Task LiveConsumer_GetsItemsAsTheyArePushed()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(16);

        var consumerTask = Task.Run(async () => await memoizer.Replay().ToListAsync());

        await Task.Yield();
        await Task.Yield();

        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        var items = await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        items.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task ErrorCompletion_LateReplay()
    {
        var error = new InvalidOperationException("test error");
        var source = CreateFailingSource(new[] { 1, 2 }, error);
        var memoizer = source.Memoize(8);

        try { await memoizer.ReadTask.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidOperationException) { }

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = new List<int>();
        var caughtError = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });

        caughtError.Should().BeSameAs(error);
        items.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Capacity_ExactlyOne()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(1);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(5);
    }

    [Fact]
    public async Task TwoLiveConsumers_BothGetAllItems()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(16);

        var consumer1 = Task.Run(async () => await memoizer.Replay().ToListAsync());
        var consumer2 = Task.Run(async () => await memoizer.Replay().ToListAsync());

        await Task.Yield();
        await Task.Yield();

        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        var items1 = await consumer1.WaitAsync(TimeSpan.FromSeconds(5));
        var items2 = await consumer2.WaitAsync(TimeSpan.FromSeconds(5));

        items1.Should().Equal(1, 2, 3, 4, 5);
        items2.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task SecondConsumer_JoinsAfterSomeItems()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(16);

        var consumer1 = Task.Run(async () => await memoizer.Replay().ToListAsync());
        await Task.Yield();

        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);

        await SpinWaitForBuffered(memoizer, 2);

        var consumer2 = Task.Run(async () => await memoizer.Replay().ToListAsync());
        await Task.Yield();

        source.Writer.TryWrite(3);
        source.Writer.TryWrite(4);
        source.Writer.Complete();

        var items1 = await consumer1.WaitAsync(TimeSpan.FromSeconds(5));
        var items2 = await consumer2.WaitAsync(TimeSpan.FromSeconds(5));

        items1.Should().Equal(1, 2, 3, 4);
        items2.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task TailSize_WithLiveConsumer()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize(16);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        // tailSize=2 after completion: gets last 2 items
        var items = await memoizer.Replay(2).ToListAsync();
        items.Should().Equal(4, 5);
    }

    [Fact]
    public async Task TailSize_Zero_WithLiveConsumer()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(16);

        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        await SpinWaitForBuffered(memoizer, 5);

        var consumer = Task.Run(async () => await memoizer.Replay(0).ToListAsync());
        await Task.Yield();

        source.Writer.TryWrite(6);
        source.Writer.TryWrite(7);
        source.Writer.Complete();

        var items = await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        items.Should().Equal(6, 7);
    }

    // === Unbounded mode tests (from AsyncMemoizer v1 patterns) ===

    [Fact]
    public async Task Unbounded_EmptyStream()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.Complete();
        var memoizer = source.Memoize();

        var items = await memoizer.Replay().ToListAsync();
        items.Should().BeEmpty();
        memoizer.IsUnbounded.Should().BeTrue();
    }

    [Fact]
    public async Task Unbounded_AllItemsKept()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 100; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize();

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(Enumerable.Range(1, 100));
    }

    [Fact]
    public async Task Unbounded_MultipleReplays()
    {
        var source = new[] { 1, 2, 3, 4, 5 }.ToAsyncEnumerable();
        var memoizer = source.Memoize();

        var items1 = await memoizer.Replay().ToListAsync();
        var items2 = await memoizer.Replay().ToListAsync();

        items1.Should().Equal(1, 2, 3, 4, 5);
        items2.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Unbounded_LiveConsumer()
    {
        var channel = Channel.CreateUnbounded<int>();
        var memoizer = channel.Memoize();

        var consumerTask = Task.Run(async () => await memoizer.Replay().ToListAsync());
        await Task.Yield();
        await Task.Yield();

        for (var i = 0; i < 10; i++)
            channel.Writer.TryWrite(i);
        channel.Writer.Complete();

        var items = await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        items.Should().Equal(Enumerable.Range(0, 10));
    }

    [Fact]
    public async Task Unbounded_ErrorCompletion()
    {
        var error = new InvalidOperationException("test error");
        var source = CreateFailingSource(new[] { 1, 2, 3 }, error);
        var memoizer = source.Memoize();

        var items = new List<int>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });

        items.Should().Equal(1, 2, 3);
        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeSameAs(error);
    }

    [Fact]
    public async Task Unbounded_GrowsBeyondInitialCapacity()
    {
        // Initial buffer is 16, push way more to force growing
        var count = 1000;
        var source = Channel.CreateUnbounded<int>();
        for (var i = 0; i < count; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize();

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(Enumerable.Range(0, count));
    }

    [Fact]
    public async Task Unbounded_CompletedEmptyChannel_Stress()
    {
        // From AsyncMemoizer v1 test
        var tasks = Enumerable.Range(0, 100).Select(async _ => {
            var source = Channel.CreateUnbounded<int>();
            source.Writer.Complete();
            var memoizer = source.Memoize();
            var target = Channel.CreateUnbounded<int>();
            await memoizer.AddReplayTarget(target, int.MaxValue)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await target.Reader.Completion
                .WaitAsync(TimeSpan.FromSeconds(5));
        }).ToArray();
        foreach (var task in tasks)
            await task;
    }

    [Fact]
    public async Task Unbounded_BasicRange()
    {
        // From AsyncMemoizer v1 BasicTest
        for (var count = 0; count <= 50; count++) {
            var source = Enumerable.Range(0, count).ToAsyncEnumerable();
            var memoizer = source.Memoize();

            var items1 = await memoizer.Replay().ToListAsync();
            var items2 = await memoizer.Replay().ToListAsync();

            items1.Should().Equal(Enumerable.Range(0, count));
            items2.Should().Equal(Enumerable.Range(0, count));
        }
    }

    [Fact]
    public async Task Unbounded_Dispose_ReturnsAllBuffers()
    {
        var source = Channel.CreateUnbounded<int>();
        // Push enough to force multiple growths (initial=16, then 32, 64, ...)
        for (var i = 0; i < 200; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        var memoizer = source.Memoize();

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Should not throw
        memoizer.Dispose();
        memoizer.Dispose();
    }

    // === Sliding window tests ===

    [Fact]
    public async Task Sliding_BasicReplay()
    {
        var items = Enumerable.Range(0, 5).ToArray();
        var memoizer = items.ToAsyncEnumerable().Memoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(items);
    }

    [Fact]
    public async Task Sliding_WindowTrims()
    {
        var items = Enumerable.Range(0, 20).ToArray();
        var memoizer = items.ToAsyncEnumerable().Memoize(8);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().HaveCount(8);
        replayed.Should().Equal(Enumerable.Range(12, 8));
    }

    [Fact]
    public async Task Sliding_ExactCapacity()
    {
        var items = Enumerable.Range(0, 10).ToArray();
        var memoizer = items.ToAsyncEnumerable().Memoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(items);
    }

    [Fact]
    public async Task Sliding_MultipleConsumers()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(100);

        // Start consumers before writes (like TwoLiveConsumers which passes)
        var consumer1 = Task.Run(async () => await memoizer.Replay().ToListAsync());
        var consumer2 = Task.Run(async () => await memoizer.Replay().ToListAsync());
        await Task.Yield();
        await Task.Yield();

        for (var i = 0; i < 8; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        var list1 = await consumer1.WaitAsync(TimeSpan.FromSeconds(5));
        var list2 = await consumer2.WaitAsync(TimeSpan.FromSeconds(5));

        list1.Should().Equal(Enumerable.Range(0, 8));
        list2.Should().Equal(Enumerable.Range(0, 8));
    }

    [Fact]
    public async Task Sliding_LateJoinerGetsOnlyBuffer()
    {
        var items = Enumerable.Range(0, 100).ToArray();
        var memoizer = items.ToAsyncEnumerable().Memoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().HaveCount(10);
        replayed.Should().Equal(Enumerable.Range(90, 10));
    }

    [Fact]
    public async Task Sliding_LiveConsumerGetsAllItems()
    {
        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize(100);

        var allItems = new List<int>();
        var consumerTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay())
                allItems.Add(item);
        });
        await Task.Yield();
        await Task.Yield();

        for (var i = 0; i < 10; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));

        allItems.Should().HaveCount(10);
        allItems.Should().Equal(Enumerable.Range(0, 10));
    }

    [Fact]
    public async Task Sliding_CompletionPropagates()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.Complete();
        var memoizer = source.Memoize(10);

        var result = await memoizer.Replay().ToListAsync();
        result.Should().Equal(1);

        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Sliding_EmptySource()
    {
        var memoizer = AsyncEnumerable.Empty<int>().Memoize(10);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().BeEmpty();
    }

    [Fact]
    public async Task Sliding_CapacityOne()
    {
        var items = Enumerable.Range(0, 50).ToArray();
        var memoizer = items.ToAsyncEnumerable().Memoize(1);
        await memoizer.WriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(49);
    }

    // === Cancellation ===

    [Fact]
    public async Task Cancellation_StopsReplay()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        var memoizer = source.Memoize(10);

        await SpinWaitForBuffered(memoizer, 1);

        using var cts = new CancellationTokenSource();
        var items = new List<int>();
        var replayTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay(cancellationToken: cts.Token))
                items.Add(item);
        });

        await Task.Delay(100);
        items.Count.Should().Be(1);

        await cts.CancelAsync();

        var act = () => replayTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>();

        source.Writer.Complete();
    }

    // === Helpers ===

    private static async Task SpinWaitForBuffered<T>(AsyncMemoizer<T> memoizer, int expectedCount, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) {
            if (memoizer.BufferedCount >= expectedCount)
                return;
            await Task.Yield();
        }
        throw new TimeoutException($"Timed out waiting for {expectedCount} buffered items, got {memoizer.BufferedCount}");
    }

    private static async IAsyncEnumerable<T> CreateFailingSource<T>(
        T[] items,
        Exception error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        throw error;
    }
}
