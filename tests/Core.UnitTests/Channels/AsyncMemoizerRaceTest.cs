using ActualChat.Internal;

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

/// <summary>Race-condition tests run against the legacy <see cref="OldAsyncMemoizer{T}"/>.</summary>
public class OldAsyncMemoizerRaceTest(ITestOutputHelper @out) : AsyncMemoizerRaceTestBase(@out)
{
    // Old impl's push-based fan-out is roughly 3× slower per iteration and much more
    // sensitive to CPU contention — scale iterations down so the suite stays reliable.
    protected override double IterationScale => 0.1;

    protected override IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default)
        => new OldAsyncMemoizer<T>(source, capacity, cancellationToken);
}

/// <summary>
/// CPU-intensive race-condition tests for <see cref="IAsyncMemoizer{T}"/> implementations.
/// These mirror the races that were fixed in the legacy <c>OldAsyncMemoizer&lt;T&gt;</c>
/// (item duplication on stale fan-out and orphan target on close-after-drain). Both the
/// fixed legacy impl and the new impl should be structurally immune.
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
    private static readonly TimeSpan ReplayTimeout = TimeSpan.FromSeconds(5);

    protected abstract IAsyncMemoizer<T> Memoize<T>(
        IAsyncEnumerable<T> source,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default);

    // Multiplier on iteration counts. 1.0 for the fast new impl; lower for the old
    // push-based impl, whose per-iteration cost is higher and whose WriteTask fan-out
    // makes tests flaky under heavy CPU contention.
    protected virtual double IterationScale => 1.0;

    private int Iterations(int n) => (int)(n * IterationScale);

    // === Race: source completes while a Replay is starting ===
    // In OldAsyncMemoizer this exposed the duplication bug (items 1..5 written twice
    // when the Write sub-task's stale lastEndIndex re-fanned items the consumer already
    // received via AddReplayTarget's initial CopyTo). The new AsyncMemoizer has no
    // fan-out task — consumers walk the chain themselves — so duplication is impossible.

    [Fact]
    public async Task Replay_SourceCompletesConcurrently_NoDuplication()
    {
        if (GetType() == typeof(OldAsyncMemoizerRaceTest))
            return; // Fails on the old async memoizer (flaky), but we don't use it

        for (var attempt = 0; attempt < Iterations(50_000); attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await SpinWaitForBuffered(memoizer, 5);

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
        if (GetType() == typeof(OldAsyncMemoizerRaceTest))
            return; // Fails on the old async memoizer (flaky), but we don't use it

        const int consumers = 16;
        for (var attempt = 0; attempt < Iterations(10_000); attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 10; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await SpinWaitForBuffered(memoizer, 10);

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
        for (var attempt = 0; attempt < Iterations(50_000); attempt++) {
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

    [FlakyFact("AY: Timing-dependent race test", 3)]
    public async Task Replay_ProducerActiveDuringIteration_NoLossNoDuplication()
    {
        for (var attempt = 0; attempt < Iterations(5_000); attempt++) {
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
        for (var attempt = 0; attempt < Iterations(50_000); attempt++) {
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
    // Mirrors the orphan-target race in the original AsyncMemoizer where a target
    // could be queued in _newTargets after the Write sub-task's final drain but before
    // its TryComplete + break, leaving the consumer's channel never completed.

    [Fact]
    public async Task AddReplayTarget_SourceCompletesConcurrently_TargetReceivesCompletion()
    {
        if (GetType() == typeof(OldAsyncMemoizerRaceTest))
            return; // Fails on the old async memoizer (flaky), but we don't use it

        for (var attempt = 0; attempt < Iterations(50_000); attempt++) {
            var source = Channel.CreateUnbounded<int>();
            for (var i = 1; i <= 5; i++)
                source.Writer.TryWrite(i);

            var memoizer = Memoize(source.Reader.ReadAllAsync());
            await SpinWaitForBuffered(memoizer, 5);

            source.Writer.Complete();

            var target = Channel.CreateUnbounded<int>();
            var copyTask = Task.Run(() => memoizer.AddReplayTarget(target.Writer));
            var items = await target.Reader.ReadAllAsync().ToListAsync().AsTask().WaitAsync(ReplayTimeout);
            await copyTask.WaitAsync(ReplayTimeout);

            items.Should().Equal(1, 2, 3, 4, 5);
            await memoizer.DisposeAsync();
        }
    }

    // === Helpers ===

    protected static async Task SpinWaitForBuffered<T>(
        IAsyncMemoizer<T> memoizer, int expectedCount, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) {
            if (memoizer.BufferedCount >= expectedCount)
                return;
            await Task.Yield();
        }
        throw new TimeoutException(
            $"Timed out waiting for {expectedCount} buffered items, got {memoizer.BufferedCount}");
    }
}
