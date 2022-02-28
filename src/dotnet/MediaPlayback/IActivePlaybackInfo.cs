namespace ActualChat.MediaPlayback;

public interface IActivePlaybackInfo
{
    [ComputeMethod]
    Task<PlayerState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken);

    void RegisterStateChange(Symbol trackId, PlayerState state);
}
