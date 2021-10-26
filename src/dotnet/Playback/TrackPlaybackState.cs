namespace ActualChat.Playback;

public record TrackPlaybackState(
    Symbol TrackId,
    Moment RecordingStartedAt,
    TimeSpan PlayingAt = default,
    bool Completed = false,
    double Volume = 1,
    double PlaybackRate = 1);
