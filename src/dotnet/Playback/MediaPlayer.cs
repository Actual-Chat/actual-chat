using ActualChat.Media;

namespace ActualChat.Playback;

public sealed class MediaPlayer : IDisposable
{
    private CancellationTokenSource _stopPlayingCts = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public Channel<MediaPlayerCommand> Queue { get; private set; } = null!;
    public Task PlayingTask { get; private set; } = null!;
    public bool IsPlaying => PlayingTask is { IsCompleted: false };
    public CancellationToken StopToken { get; private set; }
    public event Action<MediaPlayer>? StateChanged;

    public MediaPlayer(IMediaPlayerService mediaPlayerService)
    {
        MediaPlayerService = mediaPlayerService;
        Reset(false);
    }

    public void Dispose()
        => _stopPlayingCts.CancelAndDisposeSilently();

    public ValueTask AddCommand(MediaPlayerCommand command, CancellationToken cancellationToken = default)
        => Queue.Writer.WriteAsync(command, cancellationToken);

    public ValueTask AddMediaTrack(
        Symbol trackId,
        IMediaSource source,
        Moment recordingStartedAt,
        Moment? playAt,
        CancellationToken cancellationToken = default)
    {
        MediaPlayerService.RegisterDefaultMediaTrackState(new (trackId, recordingStartedAt));
        var command = new PlayMediaTrackCommand(trackId, source, recordingStartedAt, playAt);
        return AddCommand(command, cancellationToken);
    }

    public void Complete()
        => Queue.Writer.Complete();

    public Task Play()
    {
        if (!PlayingTask.IsCompleted)
            return PlayingTask;

        PlayingTask = MediaPlayerService.Play(Queue.Reader.ReadAllAsync(StopToken), StopToken);
        StateChanged?.Invoke(this);
        _ = PlayingTask.ContinueWith(_ => StateChanged?.Invoke(this), TaskScheduler.Default);
        return PlayingTask;
    }

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    public Task Stop()
    {
        var playingTask = PlayingTask;
        _stopPlayingCts.CancelAndDisposeSilently();
        Reset();
        return playingTask.SuppressExceptions();
    }

    // Private methods

    private void Reset(bool invokeStateChanged = true)
    {
        _stopPlayingCts = new ();
        StopToken = _stopPlayingCts.Token;
        Queue = Channel.CreateBounded<MediaPlayerCommand>(
            new BoundedChannelOptions(256) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        PlayingTask = Task.CompletedTask;
        StateChanged?.Invoke(this);
    }
}
