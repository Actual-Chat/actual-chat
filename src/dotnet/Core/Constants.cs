namespace ActualChat;

public static partial class Constants
{
    public static class Chat
    {
        public static Symbol DefaultChatId { get; } = "the-actual-one";
        public static Symbol AnnouncementsChatId { get; } = "announcements";
        public static TileStack<long> IdTileStack { get; } = TileStacks.Long5To1K;
        public static TileStack<Moment> TimeTileStack { get; } = TileStacks.Moment3MTo6Y;
        public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
        public const int PictureFileSizeLimit = 25 * 1024 * 1024; // 25MB
        public const int MaxSearchFilterLength = 100;
        public const int ReactionFirstAuthorIdsLimit = 10;
    }

    public static class Attachments
    {
        public const int FileSizeLimit = 25 * 1024 * 1024; // 25MB
        public const int FileCountLimit = 10;
    }

    public static class Headers
    {
        public const string ContentType = "Content-Type";
    }

    public static class Presence
    {
        public static TimeSpan UpdatePeriod { get; } = TimeSpan.FromSeconds(35);
        public static TimeSpan SkipCheckInPeriod { get; } = TimeSpan.FromSeconds(30);
        public static TimeSpan AwayTimeout { get; } = TimeSpan.FromSeconds(60);
        public static TimeSpan OfflineTimeout { get; } = TimeSpan.FromMinutes(10);
    }

    public static class Contact
    {
        public const int MaxRecentContacts = 20;
    }
}
