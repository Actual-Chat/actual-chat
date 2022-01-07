using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioTrackPlayerFactory : ITrackPlayerFactory
{
    public TrackPlayer Create(Playback playback, PlayTrackCommand playTrackCommand)
        => new AudioTrackPlayer(playback, playTrackCommand);
}
