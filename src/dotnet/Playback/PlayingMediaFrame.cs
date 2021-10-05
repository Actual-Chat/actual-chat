using ActualChat.Media;

namespace ActualChat.Playback;

public record PlayingMediaFrame(
    MediaFrame Frame,
    Moment Timestamp)
{ }
