namespace ActualChat.MediaPlayback;

/// <summary>
/// Creates <see cref="TrackPlayer"/> instances for media sources.
/// </summary>
public interface ITrackPlayerFactory
{
    TrackPlayer Create(TrackInfo trackInfo, IMediaSource source);
}
