using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface ITrackPlayerFactory
{
    TrackPlayer Create(TrackInfo trackInfo, IMediaSource source);
}
