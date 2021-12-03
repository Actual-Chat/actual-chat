namespace ActualChat.MediaPlayback;

public interface ITrackPlayerFactory
{
    TrackPlayer Create(Playback playback, PlayTrackCommand playTrackCommand);
}
