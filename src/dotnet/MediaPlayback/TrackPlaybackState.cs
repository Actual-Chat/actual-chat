namespace ActualChat.MediaPlayback;

public sealed record TrackPlaybackState(TrackPlayer TrackPlayer)
{
    public Playback Playback => TrackPlayer.Playback;
    public PlayTrackCommand Command => TrackPlayer.Command;
    public TimeSpan PlayingAt { get; init; }
    public double Volume { get; init; } = 1;
    public bool IsStarted { get; init; }
    public bool IsCompleted { get; init; }
    public Exception? Error { get; init; }

    // This record relies on referential equality
    public bool Equals(TrackPlaybackState? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
