namespace ActualChat.Playback;

public record MediaTrackPlaybackState(
    MediaPlaybackState? ParentState,
    Symbol TrackId,
    Moment RecordingStartedAt,
    TimeSpan SkipTo)
{
    public TimeSpan PlayingAt { get; init; }
    public double Volume { get; init; } = 1;
    public bool IsStarted { get; init; }
    public bool IsCompleted { get; init; }
    public Exception? Error { get; init; }
}
