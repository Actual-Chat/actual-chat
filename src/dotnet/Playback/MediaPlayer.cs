using ActualChat.Media;

namespace ActualChat.Playback;

public sealed class MediaPlayer : IAsyncDisposable
{
    private readonly ILogger<MediaPlayer> _log;
    private CancellationTokenSource _stopCts = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public Channel<MediaPlayerCommand> Queue { get; private set; } = null!;
    public Task PlayingTask { get; private set; } = null!;
    public bool IsPlaying => PlayingTask is { IsCompleted: false };
    public bool IsStopped => _stopCts.IsCancellationRequested;
    public bool IsDisposed { get; private set; }
    public CancellationToken StopToken { get; private set; }
    public event Action<MediaPlayer>? StateChanged;

    public MediaPlayer(IMediaPlayerService mediaPlayerService, ILogger<MediaPlayer> log)
    {
        _log = log;
        MediaPlayerService = mediaPlayerService;
        Reset(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        _stopCts.CancelAndDisposeSilently();
        var playingTask = PlayingTask;
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
        IMediaSource source,
        Moment recordingStartedAt,
        Moment? playAt,
        TimeSpan skipTo,
        CancellationToken cancellationToken = default)
    {
        MediaPlayerService.RegisterDefaultMediaTrackState(new (trackId, recordingStartedAt, skipTo));
        var command = new PlayMediaTrackCommand(trackId,
            source,
            recordingStartedAt,
            playAt,
            skipTo);
        return AddCommand(command, cancellationToken);
    }

    public void Complete()
        => Queue.Writer.Complete();

    public Task Play()
    {
        if (!PlayingTask.IsCompleted)
            return PlayingTask;
        AssertNotDisposed();

        PlayingTask = MediaPlayerService.Play(Queue.Reader.ReadAllAsync(StopToken), StopToken);
        StateChanged?.Invoke(this);
        _ = PlayingTask.ContinueWith(_ => StateChanged?.Invoke(this), TaskScheduler.Default);
        return PlayingTask;
    }

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    public async Task Stop()
    {
        AssertNotDisposed();
        var playingTask = PlayingTask;
        if (!playingTask.IsCompleted) {
            var stopCompletion = new TaskCompletionSource();
            var stopCommand = new StopCommand(stopCompletion);
            if (!Queue.Reader.Completion.IsCompleted) {
                await AddCommand(stopCommand, CancellationToken.None).ConfigureAwait(false);
                await stopCommand.CommandProcessed.ConfigureAwait(false);
                Complete();
            }
        }
        _stopCts.CancelAndDisposeSilently();
        Reset();
        await playingTask.SuppressExceptions();
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
        _stopCts = new ();
        StopToken = _stopCts.Token;
        Queue = Channel.CreateBounded<MediaPlayerCommand>(
            new BoundedChannelOptions(128) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        PlayingTask = Task.CompletedTask;
        StateChanged?.Invoke(this);
    }
}
