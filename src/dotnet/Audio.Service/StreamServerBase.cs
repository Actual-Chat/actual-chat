using System.Diagnostics.Metrics;

namespace ActualChat.Audio;

public abstract class StreamServerBase<TItem> : IDisposable
{
#pragma warning disable CA2213
    private readonly CancellationTokenSource _disposeTokenSource = new();
#pragma warning restore CA2213
    private readonly ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, TaskCompletionSource<AsyncMemoizer<TItem>>>> _streams = new ();

    protected int StreamBufferSize { get; init; } = 64;
    protected TimeSpan MaxStreamDuration { get; init; } = TimeSpan.FromSeconds(300);
    protected TimeSpan ReadStreamWaitDuration { get; init; } = TimeSpan.FromSeconds(2);
    protected TimeSpan ReadStreamExpiration { get; init; } = TimeSpan.FromSeconds(5);
    protected TimeSpan WriteStreamExpiration { get; init; } = TimeSpan.FromSeconds(305);

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks { get; }
    protected OtelMetrics Metrics { get; }
    protected ILogger Log { get; }

    protected UpDownCounter<int>? StreamCounter =>
        this is AudioStreamServer
            ? Metrics.AudioStreamCount
            : null;

    protected StreamServerBase(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        Metrics = services.GetRequiredService<OtelMetrics>();
    }

    public bool IsStreamExists(Symbol streamId)
        => _streams.TryGetValue(streamId, out _);

#pragma warning disable CA1816
    public virtual void Dispose()
#pragma warning restore CA1816
        => _disposeTokenSource.CancelAndDisposeSilently();

    protected async Task<IAsyncEnumerable<TItem>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        var entry = GetOrAddStream(streamId, ReadStreamExpiration);
        try {
            var storedStreamTask = entry.Value.Task;
            var isStreamExpired = storedStreamTask.IsCanceled || storedStreamTask.IsFaulted;
            if (isStreamExpired)
                return AsyncEnumerable.Empty<TItem>();

            var memoizer = await storedStreamTask
                .WaitAsync(ReadStreamWaitDuration, cancellationToken)
                .ConfigureAwait(false);
            var stream = memoizer
                .Replay(cancellationToken)
                .WithBuffer(StreamBufferSize, cancellationToken);
            return stream;
        }
        catch (TimeoutException) {
            return AsyncEnumerable.Empty<TItem>();
        }
    }

    // do not wait for write completion - just register stream!
    protected async Task Write(Symbol streamId, IAsyncEnumerable<TItem> stream, CancellationToken cancellationToken)
    {
        StreamCounter?.Add(1);
        var entry = GetOrAddStream(streamId, WriteStreamExpiration);
        if (entry.Value.Task.IsCompleted) {
            Log.LogWarning("Write({Stream}): already exists", streamId);
            return;
        }
        var memoizer = stream.Memoize(cancellationToken);
        if (!entry.Value.TrySetResult(memoizer)) {
            Log.LogWarning("Write({Stream}): already exists - unable to set result", streamId);
            return;
        }

        await memoizer.WriteTask.ConfigureAwait(false);
        _ = Clocks.CpuClock
            .Delay(ReadStreamWaitDuration, CancellationToken.None)
            .ContinueWith(
                _ => entry.Dispose(),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // Private methods

    private ExpiringEntry<Symbol, TaskCompletionSource<AsyncMemoizer<TItem>>> GetOrAddStream(
        Symbol streamId, TimeSpan expiresIn)
    {
        var entry = _streams.GetOrAdd(streamId,
            static (streamId1, arg) => {
                var (self, expiresIn) = arg;
                var memoizerSource = TaskCompletionSourceExt.New<AsyncMemoizer<TItem>>();
                var disposeTokenSource = self._disposeTokenSource.Token.CreateLinkedTokenSource();
                var entry = ExpiringEntry
                    .New(self._streams, streamId1, memoizerSource, disposeTokenSource)
                    .SetDisposer(e => {
                        if (memoizerSource.Task.IsCompleted)
                            self.StreamCounter?.Add(-1);
                        e.Value.TrySetCanceled();
                    })
                    .BumpExpiresAt(expiresIn, self.Clocks.CpuClock)
                    .BeginExpire();
                return entry;
            },
            (Self: this, ExpiresIn: expiresIn));
        return entry.BumpExpiresAt(expiresIn, Clocks.CpuClock);
    }
}
