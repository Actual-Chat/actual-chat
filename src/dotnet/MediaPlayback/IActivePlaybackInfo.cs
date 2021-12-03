namespace ActualChat.MediaPlayback;

public interface IActivePlaybackInfo
{
    [ComputeMethod]
    Task<TrackPlaybackState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken);

    void RegisterStateChange(TrackPlaybackState lastState, TrackPlaybackState state);
}
