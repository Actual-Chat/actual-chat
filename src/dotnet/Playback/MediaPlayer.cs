using ActualChat.Audio;
using ActualChat.Media;

namespace ActualChat.Playback;

public sealed class MediaPlayer : IDisposable
{
    private readonly IAudioSourceStreamer _audioSourceStreamer;
    private CancellationTokenSource _stopPlayingCts = null!;

    public IMediaPlayerService MediaPlayerService { get; }
    public MomentClockSet ClockSet { get; }
    public Channel<MediaPlayerCommand> Queue { get; private set; } = null!;
    public Task PlayingTask { get; private set; } = null!;
    public bool IsPlaying => PlayingTask is { IsCompleted: false };

    public MediaPlayer(
        IMediaPlayerService mediaPlayerService,
        IAudioSourceStreamer audioSourceStreamer,
        MomentClockSet clockSet)
    {
        _audioSourceStreamer = audioSourceStreamer;
        MediaPlayerService = mediaPlayerService;
        ClockSet = clockSet;
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

    public ValueTask AddMediaTrack(
        Symbol trackId,
        IMediaSource source,
        Moment recordingStartedAt,
        TimeSpan startOffset,
        CancellationToken cancellationToken = default)
        => AddCommand(new PlayMediaTrackCommand(trackId, source, recordingStartedAt, startOffset), cancellationToken);

    public void Complete()
        => Queue.Writer.Complete();

    public Task Play()
    {
        if (!PlayingTask.IsCompleted)
            return PlayingTask;

        var playbackChannel = Channel.CreateBounded<MediaPlayerCommand>(
            new BoundedChannelOptions(256) {
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });
        var cancellationToken = _stopPlayingCts.Token;

        _ = ResolveStreamCommands(Queue, playbackChannel, cancellationToken);
        return PlayingTask = MediaPlayerService.Play(playbackChannel.Reader.ReadAllAsync(cancellationToken),
            cancellationToken);
    }

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    public Task Stop()
    {
        var playingTask = PlayingTask;
        _stopPlayingCts.CancelAndDisposeSilently();
        Reset();
        return playingTask;
    }

    // Private methods

    private async Task ResolveStreamCommands(
        ChannelReader<MediaPlayerCommand> reader,
        ChannelWriter<MediaPlayerCommand> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var playerCommand))
                if (playerCommand is RegisterStreamCommand(var streamId, var trackId, var recordingStartedAt)) {
                    var recordingDateTime = recordingStartedAt.ToDateTime();
                    var attemptPlaybackOfStreamNotOlderThan = ClockSet.CpuClock.Now.ToDateTime().AddMinutes(-1);
                    if (recordingDateTime < attemptPlaybackOfStreamNotOlderThan)
                        continue;

                    var audioSource = await _audioSourceStreamer.GetAudioSource(streamId, cancellationToken);
                    await writer
                        .WriteAsync(
                            new PlayMediaTrackCommand(trackId, audioSource, recordingStartedAt),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                    await writer.WriteAsync(playerCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.Complete(error);
        }
    }

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
