namespace ActualChat.MediaPlayback;

public abstract record TrackPlayerCommand(TrackPlayer Player)
{ }

public record StartPlaybackCommand(TrackPlayer Player)
    : TrackPlayerCommand(Player)
{ }

public record StopPlaybackCommand(
        TrackPlayer Player,
        bool Immediately)
    : TrackPlayerCommand(Player)
{ }

public record SetTrackVolumeCommand(
        TrackPlayer Player,
        double Volume)
    : TrackPlayerCommand(Player)
{ }
