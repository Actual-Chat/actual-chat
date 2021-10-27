using ActualChat.Mathematics;

namespace ActualChat.Chat;

public static class ChatConstants
{
    public static string DefaultChatId { get; } = "the-actual-one";
    public static LogTileCover<long, long> IdTiles { get; } = LogCover.Default.Long;
    public static LogTileCover<Moment, TimeSpan> TimeTiles { get; } = LogCover.Default.Moment;
    public static TimeSpan MaxEntryDuration { get; } = TimeTiles.MinTileSize; // 3 minutes, though it can be any
}
