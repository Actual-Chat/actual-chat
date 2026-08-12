namespace ActualChat.Core.UnitTests.Channels;

public class FoldingAsyncMemoizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task FoldMatchesTheWholeSequence()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        foreach (var i in Enumerable.Range(1, 5))
            source.Writer.TryWrite(i);
        await using var memoizer = NewSum(source);
        await SpinWaitForBuffered(memoizer, 5);

        // act
        var (sum, producedCount) = memoizer.Fold();

        // assert
        sum.Should().Be(15);
        producedCount.Should().Be(5);
    }

    [Fact]
    public async Task EmptyStreamFoldsToTheSeed()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = NewSum(source);

        // act
        var (sum, producedCount) = memoizer.Fold();

        // assert
        sum.Should().Be(0);
        producedCount.Should().Be(0);
    }

    [Fact]
    public async Task RefoldCostsOnlyTheNewItems()
    {
        // arrange
        var folderCalls = 0;
        var source = Channel.CreateUnbounded<int>();
        foreach (var i in Enumerable.Range(1, 10))
            source.Writer.TryWrite(i);
        await using var memoizer = NewSum(source, () => folderCalls++);
        await SpinWaitForBuffered(memoizer, 10);
        memoizer.Fold().Value.Should().Be(55);
        folderCalls.Should().Be(10);

        // act - three more items, then refold
        folderCalls = 0;
        foreach (var i in Enumerable.Range(11, 3))
            source.Writer.TryWrite(i);
        await SpinWaitForBuffered(memoizer, 13);
        var (sum, producedCount) = memoizer.Fold();

        // assert
        sum.Should().Be(91);
        producedCount.Should().Be(13);
        folderCalls.Should().Be(3, "the first 10 are already in the checkpoint");
    }

    [Fact]
    public async Task RefoldWithNoNewItemsDoesNoWork()
    {
        // arrange
        var folderCalls = 0;
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(7);
        await using var memoizer = NewSum(source, () => folderCalls++);
        await SpinWaitForBuffered(memoizer, 1);
        memoizer.Fold();

        // act
        folderCalls = 0;
        var (sum, producedCount) = memoizer.Fold();

        // assert
        sum.Should().Be(7);
        producedCount.Should().Be(1);
        folderCalls.Should().Be(0);
    }

    [Fact]
    public async Task FoldIsStableAfterCompletion()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        source.Writer.Complete();
        await using var memoizer = NewSum(source);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        // act & assert
        memoizer.Fold().Should().Be((3, 2));
        memoizer.Fold().Should().Be((3, 2));
    }

    [Fact]
    public async Task ConcurrentFoldsAgreeAndNeverGoBackwards()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = NewSum(source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
            var lastCount = 0;
            var lastSum = 0;
            while (!cts.IsCancellationRequested) {
                var (sum, producedCount) = memoizer.Fold();
                // The fold of 1..N is always N*(N+1)/2, so a torn state shows up immediately
                sum.Should().Be(producedCount * (producedCount + 1) / 2);
                producedCount.Should().BeGreaterThanOrEqualTo(lastCount);
                sum.Should().BeGreaterThanOrEqualTo(lastSum);
                lastCount = producedCount;
                lastSum = sum;
                if (producedCount >= 500)
                    return;
            }
        }, cts.Token)).ToArray();

        // act
        for (var i = 1; i <= 500; i++) {
            source.Writer.TryWrite(i);
            if (i % 50 == 0)
                await Task.Yield();
        }

        // assert
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));
        memoizer.Fold().Should().Be((500 * 501 / 2, 500));
    }

    [Fact]
    public async Task BoundedFoldCoversTheSurvivingWindow()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = new FoldingAsyncMemoizer<int, int>(
            source.Reader.ReadAllAsync(), 0, (state, item) => state + item, capacity: 2);
        source.Writer.TryWrite(1);
        await SpinWaitForBuffered(memoizer, 1);
        memoizer.Fold().Should().Be((1, 1));

        // act - evicts past the checkpoint, so the fold restarts over what survived
        foreach (var i in Enumerable.Range(2, 4))
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        var (sum, producedCount) = memoizer.Fold();

        // assert
        memoizer.BufferedCount.Should().Be(2);
        sum.Should().Be(9); // 4 + 5
        producedCount.Should().Be(5);
    }

    [Fact]
    public async Task ReplayCollapsesTheBufferedPrefix()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        foreach (var i in Enumerable.Range(1, 10))
            source.Writer.TryWrite(i);
        await using var memoizer = NewSum(source, toItem: state => state);
        await SpinWaitForBuffered(memoizer, 10);

        // act
        var items = new List<int>();
        var readTask = Task.Run(async () => {
            await foreach (var item in memoizer.Replay())
                items.Add(item);
        });
        await SpinWaitForCount(items, 1);
        source.Writer.TryWrite(11);
        source.Writer.TryWrite(12);
        source.Writer.Complete();
        await readTask.WaitAsync(TimeSpan.FromSeconds(5));

        // assert - one folded item standing in for 1..10, then the rest verbatim
        items.Should().Equal(55, 11, 12);
    }

    [Fact]
    public async Task ReplayOfAnEmptyBufferSynthesizesNothing()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        await using var memoizer = NewSum(source, toItem: state => state);

        // act - MoveNextAsync runs the fold synchronously, so the buffer is provably empty when
        // the replay decides whether to synthesize, without racing the writer
        await using var enumerator = memoizer.Replay().GetAsyncEnumerator();
        var whenFirst = enumerator.MoveNextAsync();
        whenFirst.IsCompleted.Should().BeFalse("an empty buffer has no prefix to collapse");
        source.Writer.TryWrite(1);
        source.Writer.TryWrite(2);
        source.Writer.Complete();

        // assert
        var items = new List<int>();
        if (await whenFirst)
            items.Add(enumerator.Current);
        while (await enumerator.MoveNextAsync())
            items.Add(enumerator.Current);
        items.Should().Equal(1, 2);
    }

    [Fact]
    public async Task ReplayStaysVerbatimWithoutAnItemFactory()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        foreach (var i in Enumerable.Range(1, 5))
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await using var memoizer = NewSum(source);

        // act
        var items = await memoizer.Replay().ToListAsync();

        // assert
        items.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task BoundedReplayStaysVerbatim()
    {
        // arrange
        var source = Channel.CreateUnbounded<int>();
        foreach (var i in Enumerable.Range(1, 5))
            source.Writer.TryWrite(i);
        source.Writer.Complete();
        await using var memoizer = NewSum(source, toItem: state => state);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        // act - a tail request asks for the last N items, which a collapsed prefix can't answer
        var items = await memoizer.Replay(2).ToListAsync();

        // assert
        items.Should().Equal(4, 5);
    }

    [Fact]
    public async Task CheckpointDoesNotKeepEvictedItemsAlive()
    {
        // arrange
        var source = Channel.CreateUnbounded<object>();
        source.Writer.TryWrite(new object());
        await using var memoizer = new FoldingAsyncMemoizer<object, int>(
            source.Reader.ReadAllAsync(), 0, (state, _) => state + 1, capacity: 10);
        await SpinWaitForBuffered(memoizer, 1);
        memoizer.Fold(); // parks a checkpoint on the oldest node

        // act
        var weakRefs = new List<WeakReference>();
        for (var i = 0; i < 100; i++) {
            var item = new object();
            weakRefs.Add(new WeakReference(item));
            source.Writer.TryWrite(item);
        }
        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var aliveCount = 0;
        for (var attempt = 0; attempt < 5; attempt++) {
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);
            aliveCount = weakRefs.Take(80).Count(x => x.IsAlive);
            if (aliveCount == 0)
                break;

            await Task.Delay(50);
        }

        // assert
        aliveCount.Should().Be(0, "an evicted checkpoint must not pin the chain it points into");
    }

    // Private methods

    private static FoldingAsyncMemoizer<int, int> NewSum(
        Channel<int> source,
        Action? onFold = null,
        Func<int, int>? toItem = null)
        => new(
            source.Reader.ReadAllAsync(),
            0,
            (state, item) => {
                onFold?.Invoke();
                return state + item;
            },
            toItem);

    private static async Task SpinWaitForBuffered<T>(AsyncMemoizer<T> memoizer, int expectedCount)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000) {
            if (memoizer.BufferedCount >= expectedCount)
                return;

            await Task.Yield();
        }
        throw new TimeoutException($"Timed out waiting for {expectedCount} buffered items");
    }

    private static async Task SpinWaitForCount<T>(List<T> items, int expectedCount)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000) {
            if (items.Count >= expectedCount)
                return;

            await Task.Yield();
        }
        throw new TimeoutException($"Timed out waiting for {expectedCount} replayed items");
    }
}
