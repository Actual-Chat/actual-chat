using ActualChat.Media;

namespace ActualChat.Playback;

public sealed class MediaPlayer : IDisposable
{
    private CancellationTokenSource _stopPlayingCts = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public Channel<MediaPlayerCommand> Queue { get; private set; } = null!;
    public Task PlayingTask { get; private set; } = null!;
    public bool IsPlaying => PlayingTask is { IsCompleted: false };

    public MediaPlayer(IMediaPlayerService mediaPlayerService)
    {
        MediaPlayerService = mediaPlayerService;
        Reset();
    }

    public void Dispose()
        => _stopPlayingCts.CancelAndDisposeSilently();

    public ValueTask AddCommand(MediaPlayerCommand command, CancellationToken cancellationToken = default)
        => Queue.Writer.WriteAsync(command, cancellationToken);

    public ValueTask AddMediaTrack(
        Symbol trackId,
        IMediaSource source,
        Moment recordingStartedAt,
        CancellationToken cancellationToken = default)
        => AddCommand(new PlayMediaTrackCommand(trackId, source, recordingStartedAt), cancellationToken);

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    public void Complete()
        => Queue.Writer.Complete();

    public Task Play()
    {
        if (!PlayingTask.IsCompleted)
            return PlayingTask;

        var cancellationToken = _stopPlayingCts.Token;
        return PlayingTask = MediaPlayerService.Play(Queue.Reader.ReadAllAsync(cancellationToken), cancellationToken);
    }

    public Task Stop()
    {
        var playingTask = PlayingTask;
        _stopPlayingCts.CancelAndDisposeSilently();
        Reset();
        return playingTask.SuppressExceptions();
    }

    // Private methods

    private void Reset()
    {
        _stopPlayingCts = new CancellationTokenSource();
        PlayingTask = Task.CompletedTask;
        Queue = Channel.CreateBounded<MediaPlayerCommand>(
            new BoundedChannelOptions(256) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
    }
}
