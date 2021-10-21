using ActualChat.Media;

namespace ActualChat.Playback;

public abstract record MediaPlayerCommand { }

public record SetVolumeCommand(double Volume) : MediaPlayerCommand { }

public record PlayMediaTrackCommand(
    Symbol TrackId,
    IMediaSource Source,
    Moment RecordingStartedAt,
    TimeSpan StartOffset = default
    ) : MediaPlayerCommand
{ }

