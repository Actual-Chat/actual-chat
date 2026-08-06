using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class StreamStoreCacheTest
{
    private static readonly TimeSpan ShortExpiration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 10_000)]
    public async Task Publish_WinnerKeepsRegistration_LoserCanDispose()
    {
        // Two near-simultaneous first-viewers each construct a memoizer and
        // call Publish. Exactly one wins; the loser's source must be safely
        // disposable so the duplicate cross-shard RPC closes.
        await using var store = new StreamStore<int> { ExpirationDelay = TimeSpan.FromMinutes(5) };
        var streamId = NewStreamId();

        var ch1 = Channel.CreateUnbounded<int>();
        var ch2 = Channel.CreateUnbounded<int>();
        var winner = new AsyncMemoizer<int>(ch1.Reader.ReadAllAsync());
        var loser = new AsyncMemoizer<int>(ch2.Reader.ReadAllAsync());

        store.Publish(streamId, winner).Should().BeTrue();
        store.Publish(streamId, loser).Should().BeFalse("a memoizer is already registered");

        // Loser must be disposable cleanly (cancels its source enumerator).
        await loser.DisposeAsync();

        // Cache still resolves to the winner.
        var replay = await store.Get(streamId, false, default);
        replay.Should().NotBeNull();

        await ch1.Writer.WriteAsync(42);
        ch1.Writer.Complete();

        var items = await replay!.ToListAsync();
        items.Should().Equal(42);
    }

    [Fact(Timeout = 10_000)]
    public async Task FirstViewerCancel_DoesNotEndCacheForOthers()
    {
        // Whole point of the "pass CT.None to upstream RPC" decision: V1's
        // cancellation must NOT propagate into the cached source. Each Replay
        // owns its own CT; V2 keeps draining the memoizer.
        await using var store = new StreamStore<int> { ExpirationDelay = TimeSpan.FromMinutes(5) };
        var streamId = NewStreamId();
        var ch = Channel.CreateUnbounded<int>();
        var memoizer = new AsyncMemoizer<int>(ch.Reader.ReadAllAsync());
        store.Publish(streamId, memoizer).Should().BeTrue();

        using var v1Cts = new CancellationTokenSource();
        using var v2Cts = new CancellationTokenSource(TestTimeout);
        var v1Stream = await store.Get(streamId, false, v1Cts.Token);
        var v2Stream = await store.Get(streamId, false, v2Cts.Token);
        v1Stream.Should().NotBeNull();
        v2Stream.Should().NotBeNull();

        await ch.Writer.WriteAsync(1);
        await ch.Writer.WriteAsync(2);

        var v2Items = new List<int>();
        var v2Task = Task.Run(async () => {
            await foreach (var f in v2Stream!.WithCancellation(v2Cts.Token))
                v2Items.Add(f);
        });

        var v1FirstTwo = await TakeAsync(v1Stream!, 2, v1Cts.Token);
        v1FirstTwo.Should().Equal(1, 2);

        // V1 disconnects mid-stream.
        v1Cts.Cancel();

        // V2 must still receive subsequent items. Push 3 and 4, then end.
        await ch.Writer.WriteAsync(3);
        await ch.Writer.WriteAsync(4);
        ch.Writer.Complete();

        await v2Task.WaitAsync(TestTimeout);
        v2Items.Should().Equal(1, 2, 3, 4);
    }

    [Fact(Timeout = 10_000)]
    public async Task PublishedEntry_ExpiresAfterIdleDelay()
    {
        // After the source completes and no consumer keeps the bumper alive,
        // the entry must self-evict within ~ExpirationDelay × 2 — closing the
        // upstream RPC. Validates the lifetime ceiling described in the plan.
        await using var store = new StreamStore<int> { ExpirationDelay = ShortExpiration };
        var streamId = NewStreamId();

        var ch = Channel.CreateUnbounded<int>();
        var memoizer = new AsyncMemoizer<int>(ch.Reader.ReadAllAsync());
        store.Publish(streamId, memoizer).Should().BeTrue();
        store.Has(streamId).Should().BeTrue();

        // End the source so the memoizer's WhenRunning completes and the
        // bumper exits.
        ch.Writer.Complete();
        await (memoizer.WhenRunning ?? Task.CompletedTask).WaitAsync(TestTimeout);

        // Worst-case: bumper exits up to one period late (ExpirationDelay/2)
        // and entry expires after another ExpirationDelay. Allow 4× headroom.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(ShortExpiration.TotalSeconds * 4);
        while (store.Has(streamId) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        store.Has(streamId).Should().BeFalse("entry should expire after idle");
    }

    [Fact(Timeout = 10_000)]
    public async Task Get_NoEntry_ReturnsNullWithoutWaitForShare()
    {
        // Plumbing: Get(false) is the cache-hit fast path used by callers
        // before they fall back to a fetch. Must return null on miss
        // without blocking.
        await using var store = new StreamStore<int>();
        var streamId = NewStreamId();

        var stream = await store.Get(streamId, false, default);
        stream.Should().BeNull();
    }

    // Private methods

    private static StreamId NewStreamId()
        => StreamId.New(new NodeRef(Generate.Option));

    private static async Task<List<T>> TakeAsync<T>(
        IAsyncEnumerable<T> source, int count, CancellationToken cancellationToken)
    {
        var result = new List<T>(count);
        await foreach (var item in source.WithCancellation(cancellationToken)) {
            result.Add(item);
            if (result.Count == count)
                break;
        }
        return result;
    }
}
