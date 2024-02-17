namespace ActualChat.MediaPlayback;

public class ActivePlaybackInfo : SafeAsyncDisposableBase, IComputeService
{
    private readonly ConcurrentDictionary<Symbol, TrackInfo> _trackInfos = new();
    private readonly ConcurrentDictionary<Symbol, PlayerState> _trackPlaybackStates = new();

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual Task<PlayerState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_trackPlaybackStates.GetValueOrDefault(trackId));

    [ComputeMethod]
    public virtual Task<TrackInfo?> GetTrackInfo(Symbol trackId, CancellationToken cancellationToken)
        => Task.FromResult(_trackInfos.GetValueOrDefault(trackId));

    public virtual void RegisterStateChange(TrackInfo trackInfo, PlayerState state)
    {
        var trackId = trackInfo.TrackId;

        if (state.IsEnded) {
            _trackPlaybackStates.TryRemove(trackId, out _);
            _trackInfos.TryRemove(trackId, out _);
        }
        else {
            _trackPlaybackStates[trackId] = state;
            _trackInfos[trackId] = trackInfo;
        }

        using (Computed.Invalidate()) {
            _ = GetTrackPlaybackState(trackId, default);
            _ = GetTrackInfo(trackId, default);
        }
    }
}
