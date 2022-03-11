namespace ActualChat;

public static partial class Constants
{
    public static class Chat
    {
        public static Symbol DefaultChatId { get; } = "the-actual-one";
        public static TileStack<long> IdTileStack { get; } = TileStacks.Long16To1K;
        public static TileStack<Moment> TimeTileStack { get; } = TileStacks.Moment3MTo6Y;
        public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
    }

    public static class Attachments
    {
        public const int FileSizeLimit = 8 * 1024 * 1024;
        public const int FilesNumberLimit = 10;
    }
}
