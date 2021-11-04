namespace ActualChat.Playback;

public record MediaTrackPlaybackState(Symbol TrackId, Moment RecordingStartedAt)
{
    public double Volume { get; init; } = 1;
    public double PlaybackRate { get; init; } = 1;
    public TimeSpan PlayingAt { get; init; }
    public bool IsStarted { get; init; }
    public bool IsCompleted { get; init; }
    public Exception? Error { get; init; }
}
