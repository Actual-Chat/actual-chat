using ActualChat.Mathematics;

namespace ActualChat.Playback;

public static class PlaybackConstants
{
    public static LogCover<Moment, TimeSpan> TimestampLogCover { get; } = LogCover.Default.Moment;
}
