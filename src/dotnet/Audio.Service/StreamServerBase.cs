namespace ActualChat.Audio;

public abstract class StreamServerBase<TItem> : IDisposable
{
    private readonly CancellationTokenSource _disposeCts = new ();
    private readonly ConcurrentDictionary<Symbol, Expiring<Symbol, Task<AsyncMemoizer<TItem>>>> _streams = new ();

    protected int StreamBufferSize { get; init; } = 64;
    protected TimeSpan MaxStreamDuration { get; init; } = TimeSpan.FromSeconds(600);
    protected TimeSpan ReadStreamWaitDuration { get; init; } = TimeSpan.FromSeconds(2);
    protected TimeSpan ReadStreamExpiration { get; init; } = TimeSpan.FromSeconds(5);
    protected TimeSpan WriteStreamExpiration { get; init; } = TimeSpan.FromSeconds(605);

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks { get; }
    protected ILogger Log { get; }

    protected StreamServerBase(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
    }

    public void Dispose()
        => _disposeCts.CancelAndDisposeSilently();

    protected async Task<IAsyncEnumerable<TItem>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        var entry = GetOrAddStream(streamId, ReadStreamExpiration);
        try {
            var memoizer = await entry.Value.WaitAsync(ReadStreamWaitDuration, cancellationToken).ConfigureAwait(false);
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
    protected Task Write(Symbol streamId, IAsyncEnumerable<TItem> stream, CancellationToken cancellationToken)
    {
        var entry = GetOrAddStream(streamId, WriteStreamExpiration);
        var memoizer = stream.Memoize(cancellationToken);
        TaskSource.For(entry.Value).SetResult(memoizer);

        BackgroundTask.Run(async () => {
                await memoizer.WriteTask.ConfigureAwait(false);
                await Clocks.CpuClock.Delay(ReadStreamWaitDuration, CancellationToken.None).ConfigureAwait(false);
                entry.Dispose();
            },
            Log,
            $"{nameof(Write)} failed",
            CancellationToken.None);

        return Task.CompletedTask;
    }

    // Private methods

    private Expiring<Symbol, Task<AsyncMemoizer<TItem>>> GetOrAddStream(Symbol streamId, TimeSpan expiresIn)
    {
        var entry = _streams.GetOrAdd(streamId,
            static (streamId1, arg) => {
                var (self, expiresIn) = arg;
                var memoizerTask = TaskSource.New<AsyncMemoizer<TItem>>(true).Task;
                var disposeTokenSource = self._disposeCts.Token.CreateLinkedTokenSource();
                var entry = Expiring
                    .New(self._streams, streamId1, memoizerTask, disposeTokenSource)
                    .SetDisposer(e => TaskSource.For(e.Value).TrySetCanceled())
                    .BumpExpiresAt(expiresIn, self.Clocks.CpuClock)
                    .BeginExpire();
                return entry;
            },
            (Self: this, ExpiresIn: expiresIn));
        return entry.BumpExpiresAt(expiresIn, Clocks.CpuClock);
    }
}
