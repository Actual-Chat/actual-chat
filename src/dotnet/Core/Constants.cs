namespace ActualChat;

public static partial class Constants
{
    public static class Chat
    {
        public static Symbol DefaultChatId { get; } = "the-actual-one";
        public static Symbol AnnouncementsChatId { get; } = "announcements";
        public static TileStack<long> IdTileStack { get; } = TileStacks.Long16To1K;
        public static TileStack<Moment> TimeTileStack { get; } = TileStacks.Moment3MTo6Y;
        public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
        public const int PictureFileSizeLimit = 1 * 1024 * 1024;
    }

    public static class Attachments
    {
        public const int FileSizeLimit = 8 * 1024 * 1024;
        public const int FileCountLimit = 10;
    }

    public static class Headers
    {
        public const string ContentType = "Content-Type";
    }
}
