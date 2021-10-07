namespace ActualChat.Playback;

public interface IMediaTrackPlayerFactory
{
    public MediaTrackPlayer CreatePlayer(MediaTrack track);
}
