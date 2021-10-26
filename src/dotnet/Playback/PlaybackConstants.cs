using ActualChat.Mathematics;

namespace ActualChat.Playback;

public static class PlaybackConstants
{
    public static LogTileCover<Moment, TimeSpan> TimestampTiles { get; } = LogCover.Default.Moment;
}
