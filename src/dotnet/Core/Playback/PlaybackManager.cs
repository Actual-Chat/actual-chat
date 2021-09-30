using System.Collections.Concurrent;

namespace ActualChat.Playback;

public class PlaybackManager : IPlaybackManager
{
    private readonly ConcurrentDictionary<ChatId, PlaybackState> _playbackState = new();
    private readonly MomentClockSet _clocks;

    public PlaybackManager(MomentClockSet clocks)
        => _clocks = clocks;

    [ComputeMethod(KeepAliveTime = 60)]
    public virtual Task<PlaybackState> Get(ChatId chatId)
        => Task.FromResult(_playbackState.GetOrAdd(chatId,
            _ => new PlaybackState() {
                Realtime = new RealtimePlaybackState(),
                Replay = new ReplayState(),
            }));

    public Task Set(ChatId chatId, PlaybackState playbackState)
    {
        if (!playbackState.IsOn) {
            _playbackState.TryRemove(chatId, out var _);
        } else {
            _playbackState[chatId] = playbackState;
        }
        using (Computed.Invalidate())
            _ = Get(chatId);
        return Task.CompletedTask;
    }
}
