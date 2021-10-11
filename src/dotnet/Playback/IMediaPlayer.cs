using ActualChat.Media;

namespace ActualChat.Playback;

public interface IMediaPlayer : IAsyncDisposable
{
    Task EnqueueTrack(MediaTrack mediaTrack);

    Task PlayEnqueuedTracks(CancellationToken cancellationToken);

    Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken);

    Task Play(MediaTrack track, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId, Range<Moment> timestampRange,
        CancellationToken cancellationToken);
}
