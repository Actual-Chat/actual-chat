using ActualChat.Media;

namespace ActualChat.Playback;

public class AudioTrackPlayer : MediaTrackPlayer
{
    // TODO(AY): Implement this type

    protected override ValueTask OnPlayStart()
        => throw new NotImplementedException();

    protected override ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame)
        => throw new NotImplementedException();

    protected override ValueTask OnPlayStop()
        => throw new NotImplementedException();
}
