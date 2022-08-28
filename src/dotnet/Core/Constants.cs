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
        public const int MaxRecentPeerChats = 5;
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

    public static class Presence
    {
        public static RandomTimeSpan CheckInPeriod { get; } = TimeSpan.FromSeconds(50).ToRandom(TimeSpan.FromSeconds(1));
        public static TimeSpan SkipCheckInPeriod { get; } = TimeSpan.FromSeconds(30);
        public static TimeSpan CheckInTimeout { get; } = TimeSpan.FromSeconds(120);
    }

    public static class Contact
    {
        public const int MaxRecentContacts = 20;
    }
}
