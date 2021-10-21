using ActualChat.Media;

namespace ActualChat.Playback;

public record MediaTrackPlayerCommand(MediaTrackPlayer Player) { }

public record StartPlaybackCommand(MediaTrackPlayer Player)
    : MediaTrackPlayerCommand(Player)
{ }

public record StopPlaybackCommand(
        MediaTrackPlayer Player,
        bool Immediately)
    : MediaTrackPlayerCommand(Player)
{ }

public record PlayMediaFrameCommand(
        MediaTrackPlayer Player,
        MediaFrame Frame)
    : MediaTrackPlayerCommand(Player)
{ }

public record SetTrackVolumeCommand(
        MediaTrackPlayer Player,
        double Volume)
    : MediaTrackPlayerCommand(Player)
{ }
