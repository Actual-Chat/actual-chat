using ActualChat.Mathematics;

namespace ActualChat.Playback;

public static class MediaPlayConstants
{
    public static LogCover<Moment, TimeSpan> TimestampLogCover { get; } = LogCover.Default.Moment;
}
