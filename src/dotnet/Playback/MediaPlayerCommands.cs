using ActualChat.Media;

namespace ActualChat.Playback;

public abstract record MediaPlayerCommand(Task CommandProcessed)
{ }

public record SetVolumeCommand(double Volume) : MediaPlayerCommand(Task.CompletedTask)
{ }

public record StopCommand(TaskCompletionSource CommandProcessedSource) : MediaPlayerCommand(CommandProcessedSource.Task)
{ }

public record PlayMediaTrackCommand(
    Symbol TrackId,
    IMediaSource Source,
    Moment RecordingStartedAt,
    Moment? PlayAt
) : MediaPlayerCommand(Task.CompletedTask)
{ }
