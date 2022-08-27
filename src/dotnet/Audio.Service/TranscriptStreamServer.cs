using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServer : IAsyncDisposable, ITranscriptStreamServer
{
    public const int StreamBufferSize = 8;

    private readonly ConcurrentDictionary<Symbol, AsyncMemoizer<Transcript>> _transcriptStreams = new ();
    private readonly ConcurrentQueue<(Moment QueuedAt, Symbol StreamId)> _expirationQueue = new ();
    private readonly CancellationTokenSource _disposeCancellation = new ();

    private AudioSettings Settings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<TranscriptStreamServer> Log { get; }

    public TranscriptStreamServer(AudioSettings settings, MomentClockSet clocks, ILogger<TranscriptStreamServer> log)
    {
        Settings = settings;
        Clocks = clocks;
        Log = log;

        _ = BackgroundTask.Run(() => BackgroundCleanup(_disposeCancellation.Token), _disposeCancellation.Token);
    }

    public Task<Option<IAsyncEnumerable<Transcript>>> Read(
        Symbol streamId,
        CancellationToken cancellationToken)
    {
        if (!_transcriptStreams.TryGetValue(streamId, out var memoizer))
            return Task.FromResult(Option<IAsyncEnumerable<Transcript>>.None);

        return Task.FromResult(Option<IAsyncEnumerable<Transcript>>.Some(memoizer
            .Replay(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken)));
    }

    public async Task Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
    {
        var clock = Clocks.CoarseSystemClock;
        var memoizer = transcriptStream.Memoize(cancellationToken);
        if (_transcriptStreams.TryAdd(streamId, memoizer))
            _expirationQueue.Enqueue((clock.Now, streamId));

        await memoizer.WriteTask.ConfigureAwait(false);
    }

    public async Task Write(
        Symbol streamId,
        AsyncMemoizer<Transcript> memoizer)
    {
        var clock = Clocks.CoarseSystemClock;
        if (_transcriptStreams.TryAdd(streamId, memoizer))
            _expirationQueue.Enqueue((clock.Now, streamId));

        await memoizer.WriteTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _disposeCancellation.CancelAndDisposeSilently();
        return ValueTask.CompletedTask;
    }

    private async Task BackgroundCleanup(CancellationToken cancellationToken)
    {
        while (true) {
            await Task.Delay(Settings.StreamExpirationPeriod, cancellationToken).ConfigureAwait(false);

            var now = Clocks.CoarseSystemClock.Now;
            while (_expirationQueue.TryPeek(out var pair) && pair.QueuedAt - now > Settings.StreamExpirationPeriod)
                if (_expirationQueue.TryDequeue(out var toBeRemoved))
                    _transcriptStreams.TryRemove(toBeRemoved.StreamId, out _);
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
