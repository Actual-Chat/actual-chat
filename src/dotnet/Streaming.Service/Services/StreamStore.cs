using System.Diagnostics.Metrics;

namespace ActualChat.Streaming.Services;

/// <summary>
/// In-memory store for sharing active audio, video, transcript streams.
/// </summary>
public class StreamStore<TItem> : ProcessorBase
{
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, AsyncTaskMethodBuilder<AsyncMemoizer<TItem>?>>> _streams = new();

    public TimeSpan ExpirationDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShareWaitDelay { get; init; } = TimeSpan.FromSeconds(2);
    public int ReplayTailSize { get; init; } = int.MaxValue;
    public Action<StreamId> StreamIdValidator { get; init; } = static _ => { };
    public Action<StreamId> OnStreamExpire { get; init; } = static _ => { };
    public UpDownCounter<int>? StreamCount { get; init; }

    public ILogger? Log { get; init; }

    public bool Has(StreamId streamId)
    {
        StreamIdValidator.Invoke(streamId);
        return !StopToken.IsCancellationRequested && _streams.TryGetValue(streamId.Value, out _);
    }

    public Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, CancellationToken cancellationToken)
        => Get(streamId, true, cancellationToken);
    public async Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, bool waitForShare, CancellationToken cancellationToken)
    {
        var memoizer = await GetMemoizer(streamId, waitForShare, cancellationToken).ConfigureAwait(false);
        return memoizer?.Replay(ReplayTailSize, cancellationToken);
    }

    // Exposes the memoizer itself, so a caller can fold the buffered prefix instead of replaying it
    public async Task<AsyncMemoizer<TItem>?> GetMemoizer(
        StreamId streamId,
        bool waitForShare,
        CancellationToken cancellationToken)
    {
        StreamIdValidator.Invoke(streamId);
        if (StopToken.IsCancellationRequested)
            return null;

        if (!waitForShare && _streams.TryGetValue(streamId.Value, out var entry)) {
            if (!entry.Value.Task.IsCompleted)
                return null;

            return await entry.Value.Task.ConfigureAwait(false);
        }

        if (!waitForShare)
            return null;

        entry = GetOrAddStream(streamId);
        try {
            return await entry.Value.Task
                .WaitAsync(ShareWaitDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log?.LogWarning("Get(#{StreamId}): TIMEOUT waiting for stream after {Timeout}s", streamId, ShareWaitDelay.TotalSeconds);
            return null;
        }
    }

    /// <summary>
    /// Tries to register <paramref name="memoizer"/> as the cached stream for
    /// <paramref name="streamId"/>. Returns <c>true</c> iff this call won the
    /// registration; on <c>false</c> the caller is responsible for disposing
    /// <paramref name="memoizer"/> so its source enumerator is released.
    /// Callers that need to wait until the source stream ends should
    /// <c>await (memoizer.WhenRunning ?? Task.CompletedTask)</c>.
    /// </summary>
    public bool Publish(StreamId streamId, AsyncMemoizer<TItem> memoizer)
    {
        StreamIdValidator.Invoke(streamId);
        StopToken.ThrowIfCancellationRequested();

        var entry = GetOrAddStream(streamId);
        if (!entry.Value.TrySetResult(memoizer)) {
            Log?.LogWarning("Publish(#{StreamId}): already exists", streamId);
            return false;
        }

        StreamCount?.Add(1);
        Log?.LogInformation("Publish(#{StreamId}): stream registered", streamId);

        var whenMemoizerRunning = memoizer.WhenRunning ?? Task.CompletedTask;
        _ = BackgroundTask.Run(async () => {
            var bumpExpirationPeriod = ExpirationDelay / 2;
            while (true) {
                await Task.Delay(bumpExpirationPeriod).SilentAwait(false);
                entry.BumpExpiresAt(ExpirationDelay);
                if (whenMemoizerRunning.IsCompleted)
                    return;
            }
        }, CancellationToken.None);
        return true;
    }

    // Protected methods

    protected ExpiringEntry<Symbol, AsyncTaskMethodBuilder<AsyncMemoizer<TItem>?>> GetOrAddStream(StreamId streamId)
    {
        var entry = _streams.GetOrAdd(streamId.Value,
            static (key, self) => {
                var memoizerSource = AsyncTaskMethodBuilderExt.New<AsyncMemoizer<TItem>?>();
                var disposeTokenSource = self.StopToken.CreateLinkedTokenSource();
                var entry = ExpiringEntry
                    .New(self._streams, key, memoizerSource, disposeTokenSource)
                    .SetDisposer(e => {
                        if (memoizerSource.Task.IsCompleted) {
                            self.StreamCount?.Add(-1);
                            (memoizerSource.Task.Result as IDisposable)?.Dispose();
                        }
                        else
                            e.Value.TrySetResult(null);
                        var streamId = StreamId.Parse(key);
                        self.OnStreamExpire(streamId);
                    })
                    .BumpExpiresAt(self.ExpirationDelay)
                    .BeginExpire();
                return entry;
            },
            this);
        return entry;
    }
}
