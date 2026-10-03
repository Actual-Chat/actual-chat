namespace ActualChat.Core.UnitTests.Channels;

/// <summary>Race-condition tests run against <see cref="AsyncMemoizer{T}"/>.</summary>
public class AsyncMemoizerRaceTest(ITestOutputHelper @out) : AsyncMemoizerRaceTestBase(@out)
{
    protected override IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default)
        => new AsyncMemoizer<T>(source, capacity, cancellationToken);
}

/// <summary>
/// CPU-intensive race-condition tests for <see cref="IAsyncMemoizer{T}"/> implementations.
/// They mirror the races of the removed push-based memoizer (item duplication on a stale
/// fan-out, an orphaned target on close-after-drain), which the chain-walking
/// <see cref="AsyncMemoizer{T}"/> should be structurally immune to.
///
/// Each test loops many times and is sensitive to thread scheduling — to surface
/// flakiness, run multiple instances of the test process in parallel
/// (e.g. `for i in 1..8; do dotnet test ... & done; wait`).
/// </summary>
public abstract class AsyncMemoizerRaceTestBase(ITestOutputHelper @out) : TestBase(@out)
{
    // Iteration counts are sized so each test takes roughly 1–3 s on a developer
    // machine. Lower numbers expose nothing — race scheduling is rare enough that
    // the original AsyncMemoizer bug needed thousands of attempts under CPU
    // contention to reproduce reliably.
    private static readonly TimeSpan ReplayTimeout = TimeSpan.FromSeconds(5).CiScaled();

    protected abstract IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default);

    // === Race: source completes while a Replay is starting ===
    // A push-based fan-out duplicated items 1..5 here: its stale lastEndIndex re-sent items
    // the consumer had already got from the initial copy. AsyncMemoizer has no fan-out
    // task — consumers walk the chain themselves — so duplication is impossible.

    [Fact]
    public async Task Replay_SourceCompletesConcurrently_NoDuplication()
    {
        for (var attempt = 0; attempt < 50_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await memoizer.WhenBuffered(5);

            // Race the consumer start against the source completion.
            source.Writer.Complete();

            var items = await memoizer.Replay()
                .ToListAsync()
                .AsTask()
                .WaitAsync(ReplayTimeout);

            items.Should().Equal(1, 2, 3, 4, 5);
            await memoizer.DisposeAsync();
        }
    }

    // === Race: many concurrent late joiners while source is completing ===
    // Stresses the path where multiple consumers begin walking the chain while
    // the producer is still racing to publish items + completion.

    [Fact]
    public async Task Replay_ManyConcurrentLateJoiners_AllSeeFullStream()
    {
        const int consumers = 16;
        for (var attempt = 0; attempt < 10_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 10; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await memoizer.WhenBuffered(10);

            var startSignal = TaskCompletionSourceExt.New();
            var tasks = new Task<List<int>>[consumers];
            for (var i = 0; i < consumers; i++) {
                tasks[i] = Task.Run(async () => {
                    await startSignal.Task;
                    return await memoizer.Replay().ToListAsync();
                });
            }

            // Release all consumers simultaneously and complete the source.
            startSignal.SetResult();
            source.Writer.Complete();

            var results = await Task.WhenAll(tasks).WaitAsync(ReplayTimeout);
            foreach (var items in results)
                items.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

            await memoizer.DisposeAsync();
        }
    }

    // === Race: late joiner after source has already completed ===
    // A consumer that starts AFTER the producer has fully finished must still get
    // all buffered items + the completion signal (no hang).

    [Fact]
    public async Task Replay_LateJoinerAfterCompletion_GetsAllItems()
    {
        for (var attempt = 0; attempt < 50_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);
            source.Writer.Complete();

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await memoizer.WhenRunning!.WaitAsync(ReplayTimeout);

            var items = await memoizer.Replay()
                .ToListAsync()
                .AsTask()
                .WaitAsync(ReplayTimeout);

            items.Should().Equal(1, 2, 3, 4, 5);
            await memoizer.DisposeAsync();
        }
    }

    // === Race: producer publishes items while a Replay is mid-iteration ===
    // The consumer must observe items in order and exactly once, regardless of
    // how the producer's appends interleave with the consumer's wait/walk loop.

    [FlakyFact("AY: Timing-dependent race test", 5)]
    public async Task Replay_ProducerActiveDuringIteration_NoLossNoDuplication()
    {
        for (var attempt = 0; attempt < 5_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            var memoizer = Memoize(source.Reader.ReadAllAsync());

            // Start producer publishing 1000 items concurrently with the consumer.
            var producer = Task.Run(() => {
                for (var i = 1; i <= 1000; i++)
                    source.Writer.TryWrite(i);
                source.Writer.Complete();
            });

            var items = await memoizer.Replay()
                .ToListAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));

            await producer;
            items.Should().Equal(Enumerable.Range(1, 1000));
            await memoizer.DisposeAsync();
        }
    }

    // === Race: a Replay started before any items + items arrive concurrently ===
    // The consumer's first observation of the chain is the empty sentinel; it must
    // wait, then receive items as they arrive.

    [Fact]
    public async Task Replay_StartedBeforeAnyItems_ReceivesAllItems()
    {
        for (var attempt = 0; attempt < 50_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            var memoizer = Memoize(source.Reader.ReadAllAsync());

            // Start the replay first — it'll block on the empty chain.
            var replayTask = Task.Run(() => memoizer.Replay().ToListAsync().AsTask());

            // Then publish items.
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);
            source.Writer.Complete();

            var items = await replayTask.WaitAsync(ReplayTimeout);
            items.Should().Equal(1, 2, 3, 4, 5);
            await memoizer.DisposeAsync();
        }
    }

    // === Race: AddReplayTarget writes to a channel while the source is completing ===
    // Mirrors the orphan-target race of a push-based fan-out: a target queued after the
    // fan-out task's final drain was never completed, so the consumer's channel hung.

    [Fact]
    public async Task AddReplayTarget_SourceCompletesConcurrently_TargetReceivesCompletion()
    {
        for (var attempt = 0; attempt < 50_000; attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await memoizer.WhenBuffered(5);

            source.Writer.Complete();

            var target = Channel.CreateUnbounded<int>();
            var copyTask = Task.Run(() => memoizer.AddReplayTarget(target.Writer));
            var items = await target.Reader.ReadAllAsync().ToListAsync().AsTask().WaitAsync(ReplayTimeout);
            await copyTask.WaitAsync(ReplayTimeout);

            items.Should().Equal(1, 2, 3, 4, 5);
            await memoizer.DisposeAsync();
        }
    }
}
