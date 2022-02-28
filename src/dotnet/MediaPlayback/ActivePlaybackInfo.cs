namespace ActualChat.MediaPlayback;

// TODO: refactor this (?) / read below:
// (for example save info about active playbacks or use something like PlaybackRegistry / Store)
// or rename to ActiveTrackPlayingInfo (because it's not related with Playback object and can confuse a reader)
public class ActivePlaybackInfo : IActivePlaybackInfo
{
    private readonly ConcurrentDictionary<Symbol, PlayerState> _trackPlaybackStates = new();

    public virtual Task<PlayerState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_trackPlaybackStates.GetValueOrDefault(trackId));

    public void RegisterStateChange(Symbol trackId, PlayerState state)
    {
        if (state.IsCompleted)
            _trackPlaybackStates.TryRemove(trackId, out _);
        else
            _trackPlaybackStates[trackId] = state;

        using (Computed.Invalidate())
            _ = GetTrackPlaybackState(trackId, default);
    }
}
