namespace ActualChat.Playback;

public abstract record MediaTrackPlayerCommand(MediaTrackPlayer Player)
{ }

public record StartPlaybackCommand(MediaTrackPlayer Player)
    : MediaTrackPlayerCommand(Player)
{ }

public record StopPlaybackCommand(
        MediaTrackPlayer Player,
        bool Immediately)
    : MediaTrackPlayerCommand(Player)
{ }

public record SetTrackVolumeCommand(
        MediaTrackPlayer Player,
        double Volume)
    : MediaTrackPlayerCommand(Player)
{ }
