using System.Diagnostics.Metrics;

namespace ActualChat.Streaming;

public class StreamStore<TItem> : ProcessorBase
{
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, TaskCompletionSource<AsyncMemoizer<TItem>?>>> _streams = new ();

    public TimeSpan ExpirationDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShareWaitDelay { get; init; } = TimeSpan.FromSeconds(2);
    public Action<StreamId> StreamIdValidator { get; init; } = static _ => { };
    public UpDownCounter<int>? StreamCounter { get; init; }
    public ILogger? Log { get; init; }

    public bool Has(StreamId streamId)
    {
        StreamIdValidator.Invoke(streamId);
        return !StopToken.IsCancellationRequested && _streams.TryGetValue(streamId.LocalId, out _);
    }

    public Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, CancellationToken cancellationToken)
        => Get(streamId, true, cancellationToken);
    public async Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, bool waitForShare, CancellationToken cancellationToken)
    {
        StreamIdValidator.Invoke(streamId);
        if (StopToken.IsCancellationRequested)
            return null;

        if (!waitForShare && _streams.TryGetValue(streamId.LocalId, out var entry)) {
            if (!entry.Value.Task.IsCompleted)
                return null;

            var memoizer = await entry.Value.Task.ConfigureAwait(false);
            Log?.LogInformation("Get({StreamId}): got {Data}", streamId, memoizer != null ? "stream" : "null");
            return memoizer?.Replay(cancellationToken);
        }

        entry = GetOrAddStream(streamId);
        try {
            var memoizer = await entry.Value.Task
                .WaitAsync(ShareWaitDelay, cancellationToken)
                .ConfigureAwait(false);
            Log?.LogInformation("Get({StreamId}): got {Data}", streamId, memoizer != null ? "stream" : "null");
            // return memoizer?.Replay(cancellationToken);
            if (memoizer == null)
                return null;

            return DebugReplay();

            async IAsyncEnumerable<TItem>? DebugReplay()
            {
                await foreach (var item in memoizer.Replay(cancellationToken).ConfigureAwait(false)) {
                    Log?.LogInformation("Get({StreamId}): item {Item}", streamId, item);
                    yield return item;
                }
            }
        }
        catch (TimeoutException) {
            return null;
        }
    }

    public Task Publish(StreamId streamId, IAsyncEnumerable<TItem> stream)
        => Publish(streamId, stream.Memoize());
    public Task Publish(StreamId streamId, AsyncMemoizer<TItem> memoizer)
    {
        StreamIdValidator.Invoke(streamId);
        StopToken.ThrowIfCancellationRequested();

        // No need to wait for write completion here, it's enough to just register the stream
        StreamCounter?.Add(1);
        var entry = GetOrAddStream(streamId);
        if (!entry.Value.TrySetResult(memoizer)) {
            Log?.LogWarning("Publish({StreamId}): already exists", streamId);
            return Task.CompletedTask;
        }

        var writeTask = memoizer.WriteTask;
        _ = BackgroundTask.Run(async () => {
            var bumpExpirationPeriod = ExpirationDelay / 2;
            while (true) {
                await Task.Delay(bumpExpirationPeriod).SilentAwait(false);
                entry.BumpExpiresAt(ExpirationDelay);
                if (writeTask.IsCompleted)
                    return;
            }
        }, CancellationToken.None);
        return writeTask;
    }

    // Protected methods

    protected ExpiringEntry<Symbol, TaskCompletionSource<AsyncMemoizer<TItem>?>> GetOrAddStream(StreamId streamId)
    {
        var entry = _streams.GetOrAdd(streamId.LocalId,
            static (key, self) => {
                var memoizerSource = TaskCompletionSourceExt.New<AsyncMemoizer<TItem>?>();
                var disposeTokenSource = self.StopToken.CreateLinkedTokenSource();
                var entry = ExpiringEntry
                    .New(self._streams, key, memoizerSource, disposeTokenSource)
                    .SetDisposer(e => {
                        if (memoizerSource.Task.IsCompleted)
                            self.StreamCounter?.Add(-1);
                        else
                            e.Value.TrySetResult(null);
                    })
                    .BumpExpiresAt(self.ExpirationDelay)
                    .BeginExpire();
                return entry;
            },
            this);
        return entry;
    }
}
