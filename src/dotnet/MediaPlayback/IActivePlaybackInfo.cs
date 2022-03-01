namespace ActualChat.MediaPlayback;

public interface IActivePlaybackInfo
{
    [ComputeMethod]
    Task<PlayerState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken);

    // TODO: change trackId to TrackInfo ?
    void RegisterStateChange(Symbol trackId, PlayerState state);
}
