using System.Diagnostics.Metrics;

namespace ActualChat.Streaming.Services;

public class StreamStore<TItem, TMeta> : ProcessorBase
    where TMeta : class
{
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, Bucket>> _streams = new();

    public TimeSpan ExpirationDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShareWaitDelay { get; init; } = TimeSpan.FromSeconds(2);
    public Action<StreamId> StreamIdValidator { get; init; } = static _ => { };
    public Action<StreamId> OnStreamExpire { get; init; } = static _ => { };
    public UpDownCounter<int>? StreamCount { get; init; }

    public ILogger? Log { get; init; }

    public bool Has(StreamId streamId)
    {
        StreamIdValidator.Invoke(streamId);
        return !StopToken.IsCancellationRequested && _streams.TryGetValue(streamId.LocalId, out _);
    }

    public async Task<TMeta?> GetMetadata(StreamId streamId, CancellationToken cancellationToken)
        => !_streams.TryGetValue(streamId.Value, out var entry)
            ? null
            : await entry.Value.MetadataTask.WaitAsync(cancellationToken).ConfigureAwait(false);

    public bool TryGetMetadata(StreamId streamId, [MaybeNullWhen(false)] out TMeta metadata)
    {
        StreamIdValidator.Invoke(streamId);
        if (StopToken.IsCancellationRequested || !_streams.TryGetValue(streamId.LocalId, out var entry)) {
            metadata = null;
            return false;
        }

        metadata = entry.Value.Metadata;
        return metadata != null;
    }

    public bool TrySetMetadata(StreamId streamId, TMeta metadata)
    {
        StreamIdValidator.Invoke(streamId);
        if (StopToken.IsCancellationRequested || !_streams.TryGetValue(streamId.LocalId, out var entry))
            return false;

        return entry.Value.TrySetMetadata(metadata);
    }

    public Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, CancellationToken cancellationToken)
        => Get(streamId, true, cancellationToken);
    public async Task<IAsyncEnumerable<TItem>?> Get(StreamId streamId, bool waitForShare, CancellationToken cancellationToken)
    {
        StreamIdValidator.Invoke(streamId);
        if (StopToken.IsCancellationRequested)
            return null;

        if (!waitForShare && _streams.TryGetValue(streamId.LocalId, out var entry)) {
            if (!entry.Value.Content.Task.IsCompleted)
                return null;

            var memoizer = await entry.Value.Content.Task.ConfigureAwait(false);
            return memoizer?.Replay(cancellationToken);
        }

        entry = GetOrAddStream(streamId, default!);
        try {
            var memoizer = await entry.Value.Content.Task
                .WaitAsync(ShareWaitDelay, cancellationToken)
                .ConfigureAwait(false);
            return memoizer?.Replay(cancellationToken);
#if false
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
#endif
        }
        catch (TimeoutException) {
            return null;
        }
    }

    public Task Publish(StreamId streamId, TMeta? metadata, IAsyncEnumerable<TItem> stream)
        => Publish(streamId, metadata, stream.Memoize());
    public Task Publish(StreamId streamId, TMeta? metadata, AsyncMemoizer<TItem> memoizer)
    {
        StreamIdValidator.Invoke(streamId);
        StopToken.ThrowIfCancellationRequested();

        // No need to wait for write completion here, it's enough to just register the stream
        StreamCount?.Add(1);
        var entry = GetOrAddStream(streamId, metadata);
        if (!entry.Value.Content.TrySetResult(memoizer)) {
            Log?.LogWarning("Publish({StreamId}): already exists", streamId);
            return Task.CompletedTask;
        }
        if (metadata != null)
            entry.Value.TrySetMetadata(metadata);
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

    private ExpiringEntry<Symbol, Bucket> GetOrAddStream(StreamId streamId, TMeta? metadata)
    {
        var entry = _streams.GetOrAdd(streamId.Value,
            static (key, args) => {
                var (self, metadata) = ((StreamStore<TItem, TMeta> self, TMeta? metadata))args;
                var memoizerSource = AsyncTaskMethodBuilderExt.New<AsyncMemoizer<TItem>?>();
                var bucket = new Bucket(memoizerSource);
                if (metadata != null)
                    bucket.TrySetMetadata(metadata);
                var disposeTokenSource = self.StopToken.CreateLinkedTokenSource();
                var entry = ExpiringEntry
                    .New(self._streams, key, bucket, disposeTokenSource)
                    .SetDisposer(e => {
                        if (memoizerSource.Task.IsCompleted)
                            self.StreamCount?.Add(-1);
                        else
                            e.Value.Content.TrySetResult(null);
                        var streamId = StreamId.Parse(key);
                        self.OnStreamExpire(streamId);
                    })
                    .BumpExpiresAt(self.ExpirationDelay)
                    .BeginExpire();
                return entry;
            },
            (this, metadata));
        return entry;
    }

    private record Bucket(AsyncTaskMethodBuilder<AsyncMemoizer<TItem>?> Content)
    {
        private TMeta? _metadata;
        private TaskCompletionSource<TMeta>? _taskCompletionSource;

        public TMeta? Metadata => _metadata;

        public Task<TMeta> MetadataTask {
            get {
                if (_metadata == null) {
                    var tcs = Volatile.Read(ref _taskCompletionSource);
                    if (tcs != null)
                        return tcs.Task;

                    lock(this)
                        return (_taskCompletionSource ??= new TaskCompletionSource<TMeta>()).Task;
                }

                return Task.FromResult(_metadata!);
            }
        }


        public bool TrySetMetadata(TMeta metadata)
        {
            if (Metadata != null)
                return false;

            if (metadata == null!)
                return false;

            var original = Interlocked.CompareExchange(ref _metadata, metadata, null);
            var isSet = ReferenceEquals(original, null);
            if (isSet) {
                var tcs = Volatile.Read(ref _taskCompletionSource);
                if (tcs == null)
                    lock (this)
                        tcs = _taskCompletionSource = new TaskCompletionSource<TMeta>();
                tcs.TrySetResult(metadata);
            }
            return isSet;
        }
    }
}
