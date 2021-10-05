using ActualChat.Media;

namespace ActualChat.Playback;

public interface IMediaPlayer : IAsyncDisposable
{
    Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId, Range<Moment> timestampRange,
        CancellationToken cancellationToken);
}
