namespace ActualChat.MediaPlayback;

public interface IActivePlaybackInfo : IComputeService
{
    [ComputeMethod]
    Task<PlayerState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<TrackInfo?> GetTrackInfo(
        Symbol trackId,
        CancellationToken cancellationToken);

    void RegisterStateChange(TrackInfo trackInfo, PlayerState state);
}
