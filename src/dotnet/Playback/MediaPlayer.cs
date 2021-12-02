using ActualChat.Media;

namespace ActualChat.Playback;

public sealed class MediaPlayer : IAsyncDisposable
{
    private ILogger<MediaPlayer> Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;

    private CancellationTokenSource StopCts { get; set; } = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public Channel<MediaPlayerCommand> Queue { get; private set; } = null!;
    public MediaPlaybackState PlaybackState { get; private set; } = null!;
    public bool IsDisposed { get; private set; }
    public CancellationToken StopToken { get; private set; }
    public event Action<MediaPlayer>? StateChanged;

    public MediaPlayer(IMediaPlayerService mediaPlayerService, ILogger<MediaPlayer> log)
    {
        Log = log;
        MediaPlayerService = mediaPlayerService;
        Reset(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;
        DebugLog?.LogDebug("Disposing");
        IsDisposed = true;
        StopCts.CancelAndDisposeSilently();
        var playingTask = PlaybackState.PlayingTask;
        if (playingTask is { IsCompleted: false })
            await playingTask.SuppressExceptions().ConfigureAwait(false);
    }

    public ValueTask AddCommand(MediaPlayerCommand command, CancellationToken cancellationToken = default)
    {
        AssertNotDisposed();
        return Queue.Writer.WriteAsync(command, cancellationToken);
    }

    public ValueTask AddMediaTrack(
        Symbol trackId,
        Moment playAt,
        Moment recordingStartedAt,
        IMediaSource source,
        TimeSpan skipTo,
        CancellationToken cancellationToken = default)
    {
        var command = new PlayMediaTrackCommand(trackId,
            playAt,
            recordingStartedAt,
            source,
            skipTo);
        return AddCommand(command, cancellationToken);
    }

    public void Complete()
        => Queue.Writer.Complete();

    public MediaPlaybackState Play()
    {
        if (PlaybackState.IsPlaying)
            return PlaybackState;
        AssertNotDisposed();

        PlaybackState = MediaPlayerService.Play(Queue.Reader.ReadAllAsync(StopToken), StopToken);
        StateChanged?.Invoke(this);
        _ = PlaybackState.PlayingTask.ContinueWith(_ => StateChanged?.Invoke(this), TaskScheduler.Default);
        return PlaybackState;
    }

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    public async Task Stop()
    {
        AssertNotDisposed();
        var playingTask = PlaybackState.PlayingTask;
        if (!playingTask.IsCompleted) {
            var stopCompletion = new TaskCompletionSource();
            var stopCommand = new StopCommand(stopCompletion);
            if (!Queue.Reader.Completion.IsCompleted) {
                await AddCommand(stopCommand, CancellationToken.None).ConfigureAwait(false);
                await stopCommand.CommandProcessed.ConfigureAwait(false);
                Complete();
            }
        }
        StopCts.CancelAndDisposeSilently();
        Reset();
        await playingTask.SuppressExceptions().ConfigureAwait(false);
    }

    // Private methods

    private void AssertNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    private void Reset(bool invokeStateChanged = true)
    {
        AssertNotDisposed();
        StopCts = new();
        StopToken = StopCts.Token;
        Queue = Channel.CreateBounded<MediaPlayerCommand>(
            new BoundedChannelOptions(128) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        PlaybackState = new();
        if (invokeStateChanged)
            StateChanged?.Invoke(this);
    }
}
