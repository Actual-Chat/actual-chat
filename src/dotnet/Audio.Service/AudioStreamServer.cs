namespace ActualChat.Audio;

public class AudioStreamServer: IAudioStreamServer, IAsyncDisposable
{
    public const int StreamBufferSize = 64;

    private const int MaxStreamDuration = 600;

    private readonly ConcurrentDictionary<Symbol, AsyncMemoizer<byte[]>> _audioStreams = new ();
    private readonly ConcurrentQueue<(Moment QueuedAt, Symbol StreamId)> _expirationQueue = new ();
    private readonly CancellationTokenSource _disposeCancellation = new ();

    private AudioSettings Settings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<AudioStreamServer> Log { get; }

    public AudioStreamServer(AudioSettings settings, MomentClockSet clocks, ILogger<AudioStreamServer> log)
    {
        Settings = settings;
        Clocks = clocks;
        Log = log;

        _ = BackgroundTask.Run(() => BackgroundCleanup(_disposeCancellation.Token), _disposeCancellation.Token);
    }

    public Task<Option<IAsyncEnumerable<byte[]>>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (skipTo > TimeSpan.FromSeconds(MaxStreamDuration))
            return Task.FromResult(Option<IAsyncEnumerable<byte[]>>.Some(AsyncEnumerable.Empty<byte[]>()));

        if (!_audioStreams.TryGetValue(streamId, out var memoizer))
            return Task.FromResult(Option<IAsyncEnumerable<byte[]>>.None);

        var audioStream = memoizer
            .Replay(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        return Task.FromResult(Option<IAsyncEnumerable<byte[]>>.Some(SkipTo(audioStream, skipTo)));
    }

    public async Task Write(
        Symbol streamId,
        IAsyncEnumerable<byte[]> audioStream,
        CancellationToken cancellationToken)
    {
        var clock = Clocks.CoarseSystemClock;
        var memoizer = audioStream.Memoize(cancellationToken);
        if (_audioStreams.TryAdd(streamId, memoizer))
            _expirationQueue.Enqueue((clock.Now, streamId));

        await memoizer.WriteTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _disposeCancellation.CancelAndDisposeSilently();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Expects 20ms packets
    /// </summary>
    /// <param name="audioStream">stream of 20ms long Opus packets</param>
    /// <param name="skipTo"></param>
    /// <returns>Stream without skipped packets</returns>
    private static IAsyncEnumerable<byte[]> SkipTo(
        IAsyncEnumerable<byte[]> audioStream,
        TimeSpan skipTo)
    {
        if (skipTo <= TimeSpan.Zero)
            return audioStream;

        var skipToFrameN = (int)skipTo.TotalMilliseconds / 20;
        return audioStream
            .SkipWhile((_, i) => i < skipToFrameN);
    }

    private async Task BackgroundCleanup(CancellationToken cancellationToken)
    {
        while (true) {
            await Task.Delay(Settings.StreamExpirationPeriod, cancellationToken).ConfigureAwait(false);

            var now = Clocks.CoarseSystemClock.Now;
            while (_expirationQueue.TryPeek(out var pair) && pair.QueuedAt - now > Settings.StreamExpirationPeriod)
                if (_expirationQueue.TryDequeue(out var toBeRemoved))
                    _audioStreams.TryRemove(toBeRemoved.StreamId, out _);
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
