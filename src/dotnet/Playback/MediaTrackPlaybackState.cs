namespace ActualChat.Playback;

public record MediaTrackPlaybackState(
    Symbol TrackId,
    Moment RecordingStartedAt,
    TimeSpan PlayingAt = default,
    bool IsCompleted = false,
    double Volume = 1,
    double PlaybackRate = 1);
