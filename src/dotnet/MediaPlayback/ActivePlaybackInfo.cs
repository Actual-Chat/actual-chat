using System.Collections.Concurrent;

namespace ActualChat.MediaPlayback;

public class ActivePlaybackInfo : IActivePlaybackInfo
{
    private readonly ConcurrentDictionary<Symbol, TrackPlaybackState> _trackPlaybackStates = new ();

    public virtual Task<TrackPlaybackState?> GetTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_trackPlaybackStates.GetValueOrDefault(trackId));

    public void RegisterStateChange(TrackPlaybackState lastState, TrackPlaybackState state)
    {
        var trackId = state.Command.TrackId;
        if (state.IsCompleted)
            _trackPlaybackStates.TryRemove(trackId, out _);
        else
            _trackPlaybackStates[trackId] = state;

        using (Computed.Invalidate())
            _ = GetTrackPlaybackState(trackId, default);
    }
}
