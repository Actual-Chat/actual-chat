using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface ITrackPlayerFactory
{
    TrackPlayer Create(IMediaSource source);
}
