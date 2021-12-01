using System.Collections.Concurrent;
using Stl.Comparison;

namespace ActualChat.Playback;

public class MediaPlaybackState
{
    private volatile int _playingTrackCount;

    public long PlayingTrackCount => _playingTrackCount;
    public ConcurrentDictionary<Ref<PlayMediaTrackCommand>, MediaTrackPlayer> TrackPlayers { get; } = new();
    public CancellationToken StopToken { get; internal set; }
    public Task PlayingTask { get; internal set; } = Task.CompletedTask;
    public bool IsPlaying => !PlayingTask.IsCompleted;

    // Private & internal methods

    internal void IncrementPlayingTrackCount()
        => Interlocked.Increment(ref _playingTrackCount);
    internal void DecrementPlayingTrackCount()
        => Interlocked.Decrement(ref _playingTrackCount);
}
