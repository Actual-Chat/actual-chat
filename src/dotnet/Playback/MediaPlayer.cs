namespace ActualChat.Playback;

public sealed class MediaPlayer : IDisposable
{
    private CancellationTokenSource _stopPlayingCts = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public Channel<MediaTrack> Queue { get; private set; } = null!;
    public Task PlayingTask { get; private set; } = null!;
    public bool IsPlaying => PlayingTask is { IsCompleted: false };

    public MediaPlayer(IMediaPlayerService mediaPlayerService)
    {
        MediaPlayerService = mediaPlayerService;
        Reset();
    }

    public void Dispose()
        => _stopPlayingCts.CancelAndDisposeSilently();

    public ValueTask AddMediaTrack(MediaTrack mediaTrack, CancellationToken cancellationToken = default)
        => Queue.Writer.WriteAsync(mediaTrack, cancellationToken);

    public void AddEndOfPlay()
        => Queue.Writer.TryComplete();

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
        return playingTask;
    }

    // Private methods

    private void Reset()
    {
        _stopPlayingCts = new();
        PlayingTask = Task.CompletedTask;
        Queue = Channel.CreateBounded<MediaTrack>(
            new BoundedChannelOptions(256) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
            });
    }
}
