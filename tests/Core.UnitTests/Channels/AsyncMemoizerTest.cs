using ActualChat.Internal;

namespace ActualChat.Core.UnitTests.Channels;

/// <summary>Shared tests run against <see cref="AsyncMemoizer{T}"/>.</summary>
public class AsyncMemoizerTest(ITestOutputHelper @out) : AsyncMemoizerTestBase(@out)
{
    protected override bool IsItemDropInstant => true;

    protected override IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default)
        => new AsyncMemoizer<T>(source, capacity, cancellationToken);

    [Fact]
    public async Task BoundedReplay_StalledConsumerDoesNotKeepEvictedChainAlive()
    {
        var source = Channel.CreateUnbounded<object>();
        source.Writer.TryWrite(new object());
        await using var memoizer = Memoize(source, 10);
        await SpinWaitForBuffered(memoizer, 1);

        var firstItem = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var replayTask = Task.Run(async () => {
            await foreach (var _ in memoizer.Replay()) {
                firstItem.SetResult();
                await gate.Task.ConfigureAwait(false);
                break;
            }
        });

        await firstItem.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var weakRefs = PopulateChannelWithTrackedObjects(source, 100);
        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var oldEvictedRefs = weakRefs.Take(80).ToList();
        var aliveCount = 0;
        for (var attempt = 0; attempt < 5; attempt++) {
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);
            aliveCount = oldEvictedRefs.Count(wr => wr.IsAlive);
            if (aliveCount == 0)
                break;

            await Task.Delay(50);
        }

        gate.SetResult();
        await replayTask.WaitAsync(TimeSpan.FromSeconds(5));
        aliveCount.Should().Be(0, "a stalled consumer should not retain the evicted linked-list tail");
    }

    [Fact]
    public async Task FoldBufferedReturnsBufferedItemsAndProducedCount()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        source.Writer.TryWrite(3);
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());
        await SpinWaitForBuffered(memoizer, 3);

        // act - the source is still open, so only the buffered prefix must be folded
        var (sum, producedCount) = memoizer.FoldBuffered(0, static (state, item) => state + item);

        // assert
        sum.Should().Be(6);
        producedCount.Should().Be(3);
        memoizer.ProducedCount.Should().Be(3);
    }

    [Fact]
    public async Task FoldBufferedFollowsTheProducer()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());
        await SpinWaitForBuffered(memoizer, 1);
        var (sum, producedCount) = memoizer.FoldBuffered(0, static (state, item) => state + item);
        sum.Should().Be(1);
        producedCount.Should().Be(1);

        // act
        source.Writer.TryWrite(2);
        await SpinWaitForBuffered(memoizer, 2);
        (sum, producedCount) = memoizer.FoldBuffered(0, static (state, item) => state + item);

        // assert
        sum.Should().Be(3);
        producedCount.Should().Be(2);
    }

    [Fact]
    public async Task FoldBufferedSkipsEvictedItems()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync(), 2);
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        // act
        var (sum, producedCount) = memoizer.FoldBuffered(0, static (state, item) => state + item);

        // assert
        memoizer.BufferedCount.Should().Be(2);
        sum.Should().Be(9); // 4 + 5, the window left by capacity 2
        producedCount.Should().Be(5);
    }

    [Fact]
    public async Task WhenChangedCompletesOnNextItem()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());
        await SpinWaitForBuffered(memoizer, 1);

        // act
        var whenChanged = memoizer.WhenChanged(1);
        whenChanged.IsCompleted.Should().BeFalse();
        source.Writer.TryWrite(2);

        // assert
        await whenChanged.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.ProducedCount.Should().Be(2);
    }

    [Fact]
    public async Task WhenChangedIsCompletedWhenProducerMovedAhead()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());
        await SpinWaitForBuffered(memoizer, 2);

        // act
        var whenChanged = memoizer.WhenChanged(1);

        // assert
        whenChanged.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task WhenChangedCompletesOnStreamCompletion()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        await using var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());
        await SpinWaitForBuffered(memoizer, 1);
        var whenChanged = memoizer.WhenChanged(1);

        // act
        source.Writer.Complete();

        // assert
        await whenChanged.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
    }
}

/// <summary>Shared tests run against the legacy <see cref="OldAsyncMemoizer{T}"/>.</summary>
public class OldAsyncMemoizerTest(ITestOutputHelper @out) : AsyncMemoizerTestBase(@out)
{
    protected override bool IsItemDropInstant => true;

    protected override IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default)
        => new OldAsyncMemoizer<T>(source, capacity, cancellationToken);
}

/// <summary>
/// Common tests for <see cref="IAsyncMemoizer{T}"/> implementations. Subclasses at
/// the top of this file supply the factory and declare whether item drops happen
/// instantly when bounded capacity overflows (old impl) or lazily once lagging
/// consumers release their local pointers (new impl).
/// </summary>
public abstract class AsyncMemoizerTestBase(ITestOutputHelper @out) : TestBase(@out)
{
    /// <summary>Construct the memoizer under test.</summary>
    protected abstract IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when overflowing bounded capacity physically evicts items from the buffer
    /// immediately. False when evicted items are kept alive by any lagging consumer
    /// that still holds a reference.
    /// </summary>
    protected abstract bool IsItemDropInstant { get; }

    protected IAsyncMemoizer<T> Memoize<T>(Channel<T> channel, int capacity = int.MaxValue, CancellationToken ct = default)
        => Memoize(channel.Reader.ReadAllAsync(ct), capacity, ct);

    // === Basic tests (bounded) ===

    [Fact]
    public async Task EmptyStream_Completes()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.Complete();
        await using var memoizer = Memoize(source, 8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().BeEmpty();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeOfType<ChannelClosedException>();
    }

    [Fact]
    public async Task SingleItem()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.Complete();
        await using var memoizer = Memoize(source, 8);

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
        await using var memoizer = Memoize(source, 8);

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
        await using var memoizer = Memoize(source, 4);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
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
        await using var memoizer = Memoize(source, 16);

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
        await using var memoizer = Memoize(source, 16);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

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
        await using var memoizer = Memoize(source, 16);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

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
        await using var memoizer = Memoize(source, 8);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay(100).ToListAsync();
        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ErrorCompletion()
    {
        var error = new InvalidOperationException("test error");
        await using var memoizer = Memoize(CreateFailingSource(new[] { 1, 2, 3 }, error), 8);

        var items = new List<int>();
        var caughtError = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });

        caughtError.Should().BeSameAs(error);
        items.Should().Equal(1, 2, 3);

        // WhenRunning may or may not propagate the exception depending on the impl
        // (new impl's single Read task rethrows; old impl's Write sub-task completes
        // normally even when Read faulted). Waiting for it is enough — the memoizer
        // should report the error via Completion either way.
        try { await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidOperationException) { }

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
        await using var memoizer = Memoize(source, 8);

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(1, 2);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
        memoizer.Completion.Should().BeOfType<ChannelClosedException>();
    }

    [Fact]
    public async Task Dispose_IsSafeToCallTwice()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.Complete();
        var memoizer = Memoize(source, 8);

        await memoizer.Replay().ToListAsync();

        // DisposeAsync awaits WhenRunning internally — no need for an explicit wait.
        await memoizer.DisposeAsync();
        await memoizer.DisposeAsync(); // double dispose safe
    }

    [Fact]
    public async Task Capacity_RingBufferEvictsOldItems()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 10; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await using var memoizer = Memoize(source, 3);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(8, 9, 10);
    }

    [Fact]
    public async Task Memoize_FromAsyncEnumerable()
    {
        await using var memoizer = Memoize(new[] { 1, 2, 3 }.ToAsyncEnumerable(), 8);

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
        await using var memoizer = Memoize(source, 8);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        for (var r = 0; r < 3; r++) {
            var items = await memoizer.Replay().ToListAsync();
            items.Should().Equal(1, 2, 3, 4, 5);
        }
    }

    [Fact]
    public async Task LiveConsumer_GetsItemsAsTheyArePushed()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 16);

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
        await using var memoizer = Memoize(CreateFailingSource(new[] { 1, 2 }, error), 8);

        try { await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidOperationException) { }

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
        await using var memoizer = Memoize(source, 1);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(5);
    }

    [Fact]
    public async Task TwoLiveConsumers_BothGetAllItems()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 16);

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
        await using var memoizer = Memoize(source, 16);

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
        await using var memoizer = Memoize(source, 16);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay(2).ToListAsync();
        items.Should().Equal(4, 5);
    }

    // === Unbounded mode tests ===

    [Fact]
    public async Task Unbounded_EmptyStream()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.Complete();
        await using var memoizer = Memoize(source);

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
        await using var memoizer = Memoize(source);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(Enumerable.Range(1, 100));
    }

    [Fact]
    public async Task Unbounded_MultipleReplays()
    {
        await using var memoizer = Memoize(new[] { 1, 2, 3, 4, 5 }.ToAsyncEnumerable());

        var items1 = await memoizer.Replay().ToListAsync();
        var items2 = await memoizer.Replay().ToListAsync();

        items1.Should().Equal(1, 2, 3, 4, 5);
        items2.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Unbounded_LiveConsumer()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(channel);

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
        await using var memoizer = Memoize(CreateFailingSource(new[] { 1, 2, 3 }, error));

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
        const int count = 1000;
        var source = Channel.CreateUnbounded<int>();
        for (var i = 0; i < count; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await using var memoizer = Memoize(source);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay().ToListAsync();
        items.Should().Equal(Enumerable.Range(0, count));
    }

    [Fact]
    public async Task Unbounded_CompletedEmptyChannel_Stress()
    {
        var tasks = Enumerable.Range(0, 100).Select(async _ => {
            var source = Channel.CreateUnbounded<int>();
            source.Writer.Complete();
            await using var memoizer = Memoize(source);
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
        for (var count = 0; count <= 50; count++) {
            await using var memoizer = Memoize(Enumerable.Range(0, count).ToAsyncEnumerable());

            var items1 = await memoizer.Replay().ToListAsync();
            var items2 = await memoizer.Replay().ToListAsync();

            items1.Should().Equal(Enumerable.Range(0, count));
            items2.Should().Equal(Enumerable.Range(0, count));
        }
    }

    // === Sliding window tests ===

    [Fact]
    public async Task Sliding_BasicReplay()
    {
        var items = Enumerable.Range(0, 5).ToArray();
        await using var memoizer = Memoize(items.ToAsyncEnumerable(), 10);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(items);
    }

    [Fact]
    public async Task Sliding_WindowTrims()
    {
        var items = Enumerable.Range(0, 20).ToArray();
        await using var memoizer = Memoize(items.ToAsyncEnumerable(), 8);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().HaveCount(8);
        replayed.Should().Equal(Enumerable.Range(12, 8));
    }

    [Fact]
    public async Task Sliding_ExactCapacity()
    {
        var items = Enumerable.Range(0, 10).ToArray();
        await using var memoizer = Memoize(items.ToAsyncEnumerable(), 10);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(items);
    }

    [Fact]
    public async Task Sliding_MultipleConsumers()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 100);

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
        await using var memoizer = Memoize(items.ToAsyncEnumerable(), 10);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().HaveCount(10);
        replayed.Should().Equal(Enumerable.Range(90, 10));
    }

    [Fact]
    public async Task Sliding_LiveConsumerGetsAllItems()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 100);

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
        await using var memoizer = Memoize(source, 10);

        var result = await memoizer.Replay().ToListAsync();
        result.Should().Equal(1);

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Sliding_EmptySource()
    {
        await using var memoizer = Memoize(AsyncEnumerable.Empty<int>(), 10);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().BeEmpty();
    }

    [Fact]
    public async Task Sliding_CapacityOne()
    {
        var items = Enumerable.Range(0, 50).ToArray();
        await using var memoizer = Memoize(items.ToAsyncEnumerable(), 1);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay().ToListAsync();
        replayed.Should().Equal(49);
    }

    // === Cancellation ===

    [Fact]
    public async Task Cancellation_StopsReplay()
    {
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        await using var memoizer = Memoize(source, 10);

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

    [Fact]
    public async Task Cancellation_CompletesWriteTask()
    {
        using var cts = new CancellationTokenSource();
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        await using var memoizer = Memoize(source, 10, cts.Token);

        await SpinWaitForBuffered(memoizer, 2);

        await cts.CancelAsync();

        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        memoizer.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_LateJoinerCanReplayBufferedItems()
    {
        using var cts = new CancellationTokenSource();
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);

        await using var memoizer = Memoize(source, 10, cts.Token);
        await SpinWaitForBuffered(memoizer, 5);

        await cts.CancelAsync();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var replayed = await memoizer.Replay()
            .ToListAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        replayed.Should().Equal(1, 2, 3, 4, 5);
    }

    // === AddReplayTarget completion races ===

    [Fact]
    public async Task AddReplayTarget_SourceCompletesWhileRegistering()
    {
        for (var attempt = 0; attempt < 50; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);

            await using var memoizer = Memoize(source, 10);
            await SpinWaitForBuffered(memoizer, 5);

            source.Writer.Complete();

            var items = await memoizer.Replay()
                .ToListAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2));

            items.Should().Equal(1, 2, 3, 4, 5);
        }
    }

    [Fact]
    public async Task AddReplayTarget_LateJoinerAfterCompletion()
    {
        var source = Channel.CreateUnbounded<int>();
        for (var i = 1; i <= 3; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        await using var memoizer = Memoize(source, 10);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var items = await memoizer.Replay()
            .ToListAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        items.Should().Equal(1, 2, 3);
    }

    // === AddReplayTarget fan-out ===

    [Fact]
    public async Task AddReplayTarget_AllTargetsGetAllItems()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 1000);

        const int targetCount = 5;
        var channels = new Channel<int>[targetCount];
        var registerTasks = new Task[targetCount];
        for (var i = 0; i < targetCount; i++) {
            var ch = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
            channels[i] = ch;
            registerTasks[i] = memoizer.AddReplayTarget(ch.Writer, 0);
        }

        // For pull-based impls the register-tasks never finish until the source completes,
        // so we only wait for them at the end.
        await Task.Delay(50);

        for (var i = 1; i <= 100; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        foreach (var ch in channels) {
            var items = await ch.Reader.ReadAllAsync().ToListAsync()
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            items.Should().Equal(Enumerable.Range(1, 100));
        }
        await Task.WhenAll(registerTasks).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LiveConsumer_Over1024Items_NoStall()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(channel.Reader.ReadAllAsync());

        var consumedCount = 0;
        var consumerTask = Task.Run(async () => {
            var count = 0;
            await foreach (var item in memoizer.Replay()) {
                count++;
                Interlocked.Exchange(ref consumedCount, count);
            }
        });

        const int targetCount = 1200;
        for (var i = 0; i < targetCount; i++) {
            channel.Writer.TryWrite(i);
            await Task.Delay(1);

            if (i > 0 && i % 200 == 0) {
                var consumed = Volatile.Read(ref consumedCount);
                var lag = i - consumed;
                Out.WriteLine($"[{i}] consumed={consumed} lag={lag}");
                lag.Should().BeLessThan(400, $"Consumer stalled at {consumed} while producer at {i}");
            }
        }
        channel.Writer.Complete();

        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref consumedCount).Should().Be(targetCount);
    }

    [Fact]
    public async Task TwoLayerMemoizer_NoStall()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var memoizer1 = Memoize(channel.Reader.ReadAllAsync());
        await using var memoizer2 = Memoize(memoizer1.Replay());

        var consumedCount = 0;
        var consumerTask = Task.Run(async () => {
            var count = 0;
            await foreach (var item in memoizer2.Replay()) {
                count++;
                Interlocked.Exchange(ref consumedCount, count);
            }
        });

        const int targetCount = 1200;
        for (var i = 0; i < targetCount; i++) {
            channel.Writer.TryWrite(i);
            await Task.Delay(1);

            if (i > 0 && i % 200 == 0) {
                var consumed = Volatile.Read(ref consumedCount);
                var lag = i - consumed;
                Out.WriteLine($"[{i}] consumed={consumed} lag={lag}");
                lag.Should().BeLessThan(400, $"Consumer stalled at {consumed} while producer at {i}");
            }
        }
        channel.Writer.Complete();

        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref consumedCount).Should().Be(targetCount);
    }

    // === Bounded capacity overflow + slow consumer ===
    // IsItemDropInstant=true: bounded overflow physically evicts items;
    //     the consumer sees whatever remained in the bounded window when it resumed, with a gap.
    // IsItemDropInstant=false: the consumer holds evicted nodes alive via its local
    //     pointer and sees every item produced (the stall just delays delivery).
    // Either way, a *new* late-joiner sees only the current buffer (last capacity items).

    [Fact]
    public async Task BoundedReplay_SlowConsumerUnderCapacityOverflow()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 10);

        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        await SpinWaitForBuffered(memoizer, 5);

        var firstItem = new TaskCompletionSource<int>();
        var gate = new TaskCompletionSource();
        var items = new List<int>();
        var replayTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay()) {
                if (!firstItem.Task.IsCompleted) {
                    firstItem.SetResult(item);
                    await gate.Task.ConfigureAwait(false);
                }
                items.Add(item);
            }
        });

        await firstItem.Task.WaitAsync(TimeSpan.FromSeconds(5));

        for (var i = 6; i <= 50; i++)
            source.Writer.TryWrite(i);

        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        await replayTask.WaitAsync(TimeSpan.FromSeconds(5));

        if (IsItemDropInstant) {
            items.First().Should().Be(1, "first item was read before blocking");
            items.Last().Should().Be(50, "most recent item should be present");
            items.Should().HaveCountLessThan(50, "some items should be skipped due to bounded eviction");
        }
        else {
            items.Should().Equal(Enumerable.Range(1, 50));
        }

        // A new late joiner sees only what's currently buffered — identical for both modes.
        var lateItems = await memoizer.Replay()
            .ToListAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        lateItems.Should().Equal(Enumerable.Range(41, 10));
    }

    // === TailSize = 0 with a live consumer ===

    [Fact]
    public async Task TailSize_Zero_WithLiveConsumer()
    {
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = Memoize(source, 16);

        for (var i = 1; i <= 5; i++)
            source.Writer.TryWrite(i);
        await SpinWaitForBuffered(memoizer, 5);

        var replayChannel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
        var copyTask = Task.Run(() => memoizer.AddReplayTarget(replayChannel.Writer, 0));

        // Give AddReplayTarget a moment to register / reach its first await.
        await Task.Delay(100);

        source.Writer.TryWrite(6);
        source.Writer.TryWrite(7);
        source.Writer.Complete();

        var items = await replayChannel.Reader.ReadAllAsync().ToListAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await copyTask.WaitAsync(TimeSpan.FromSeconds(5));
        items.Should().Equal(6, 7);
    }

    // === DisposeAsync releases buffered items for GC ===

    [Fact]
    public async Task Dispose_ReleasesBufferedItems()
    {
        var (memoizer, weakRefs) = await SetupAndDispose();

        var aliveCount = 0;
        for (var attempt = 0; attempt < 5; attempt++) {
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);
            aliveCount = weakRefs.Count(wr => wr.IsAlive);
            if (aliveCount == 0)
                break;
            await Task.Delay(50);
        }

        aliveCount.Should().Be(0, "all items should be GC'd after DisposeAsync");
        GC.KeepAlive(memoizer);
        return;

        [MethodImpl(MethodImplOptions.NoInlining)]
        async Task<(IAsyncMemoizer<object> Memoizer, List<WeakReference> WeakRefs)> SetupAndDispose()
        {
            var source = Channel.CreateUnbounded<object>();
            var weakRefs = PopulateChannelWithTrackedObjects(source, 20);
            var m = Memoize(source);
            await SpinWaitForBuffered(m, 20);

            source.Writer.Complete();
            await m.DisposeAsync();
            return (m, weakRefs);
        }
    }

    [Fact]
    public async Task ItemsHeldUntilDispose()
    {
        var source = Channel.CreateUnbounded<object>();
        var earlyWeakRef = WriteEarlyItemAndGetWeakRef(source);
        for (var i = 1; i < 20; i++)
            source.Writer.TryWrite(new object());
        var memoizer = Memoize(source);
        await SpinWaitForBuffered(memoizer, 20);

        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        earlyWeakRef.IsAlive.Should().BeTrue("item is still buffered");

        source.Writer.Complete();
        await memoizer.DisposeAsync();

        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
        earlyWeakRef.IsAlive.Should().BeFalse("item should be GC'd after DisposeAsync");
    }

    // === In-flight iterations survive DisposeAsync ===

    [Fact]
    public async Task Dispose_WhileConsumerStillIterating()
    {
        var gate = new TaskCompletionSource();
        async IAsyncEnumerable<int> SlowSource([EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return 1;
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return 2;
        }

        var memoizer = Memoize(SlowSource(), 10);
        await SpinWaitForBuffered(memoizer, 1);

        var items = new List<int>();
        var replayTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });

        await Task.Delay(50);

        gate.SetResult();
        await SpinWaitForBuffered(memoizer, 2);
        await memoizer.DisposeAsync();

        var consumerCompleted = replayTask.Wait(TimeSpan.FromSeconds(2));
        consumerCompleted.Should().BeTrue("consumer should complete even after DisposeAsync");
        items.Should().Equal(1, 2);
    }

    // === No nulls during concurrent heavy appends ===

    [Fact]
    public async Task ReplayDuringHeavyAppends_NoNulls()
    {
        const int initialItems = 14;
        const int extraItems = 50;

        var source = Channel.CreateUnbounded<object>();
        for (var i = 0; i < initialItems; i++)
            source.Writer.TryWrite(new object());

        await using var memoizer = Memoize(source);
        await SpinWaitForBuffered(memoizer, initialItems);

        var replayTask = Task.Run(async () => {
            var collected = new List<object>();
            await foreach (var item in memoizer.Replay().ConfigureAwait(false)) {
                item.Should().NotBeNull();
                collected.Add(item);
                if (collected.Count >= initialItems + extraItems)
                    break;
            }
            return collected;
        });

        await Task.Delay(50);

        for (var i = 0; i < extraItems; i++)
            source.Writer.TryWrite(new object());

        var result = await replayTask.WaitAsync(TimeSpan.FromSeconds(10));
        result.Should().HaveCount(initialItems + extraItems);
        result.Should().NotContainNulls();

        source.Writer.Complete();
    }

    [Fact]
    public async Task AddReplayTargetDuringHeavyAppends_NoNulls()
    {
        const int totalItems = 100;
        var gate = new TaskCompletionSource();

        async IAsyncEnumerable<object> SlowSource([EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < 15; i++)
                yield return new object();
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            for (var i = 15; i < totalItems; i++)
                yield return new object();
        }

        await using var memoizer = Memoize(SlowSource());
        await SpinWaitForBuffered(memoizer, 15);

        var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        var copyTask = Task.Run(() => memoizer.AddReplayTarget(channel, int.MaxValue));
        await Task.Delay(50);

        gate.SetResult();

        var items = new List<object>();
        while (await channel.Reader.WaitToReadAsync())
        while (channel.Reader.TryRead(out var item))
            items.Add(item);

        await copyTask.WaitAsync(TimeSpan.FromSeconds(10));

        items.Should().HaveCount(totalItems);
        items.Should().NotContainNulls();
    }

    // === Helpers ===

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected static WeakReference WriteEarlyItemAndGetWeakRef(Channel<object> channel)
    {
        var item = new object();
        var weakRef = new WeakReference(item);
        channel.Writer.TryWrite(item);
        return weakRef;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected static List<WeakReference> PopulateChannelWithTrackedObjects(Channel<object> channel, int count)
    {
        var weakRefs = new List<WeakReference>();
        for (var i = 0; i < count; i++) {
            var obj = new object();
            weakRefs.Add(new WeakReference(obj));
            channel.Writer.TryWrite(obj);
        }
        return weakRefs;
    }

    protected static async Task SpinWaitForBuffered<T>(IAsyncMemoizer<T> memoizer, int expectedCount, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) {
            if (memoizer.BufferedCount >= expectedCount)
                return;
            await Task.Yield();
        }
        throw new TimeoutException($"Timed out waiting for {expectedCount} buffered items, got {memoizer.BufferedCount}");
    }

    protected static async IAsyncEnumerable<T> CreateFailingSource<T>(
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
