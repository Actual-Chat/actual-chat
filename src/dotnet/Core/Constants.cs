namespace ActualChat;

public static partial class Constants
{
    public static class Chat
    {
        public static ChatId DefaultChatId { get; } = new("the-actual-one");
        public static ChatId AnnouncementsChatId { get; } = new("announcements");
        public static IReadOnlySet<ChatId> SystemChatIds { get; } =
            new HashSet<ChatId>(new [] {DefaultChatId, AnnouncementsChatId});

        public static TileStack<long> IdTileStack { get; } = TileStacks.Long5To1K;
        public static TileStack<Moment> TimeTileStack { get; } = TileStacks.Moment3MTo6Y;
        public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
        public const int PictureFileSizeLimit = 25 * 1024 * 1024; // 25MB
        public const int MaxSearchFilterLength = 100;
        public const int ReactionFirstAuthorIdsLimit = 10;
    }

    public static class User
    {
        public static class Admin
        {
            public static UserId UserId { get; } = new("actualadmin", AssumeValid.Option);
            public static string Name { get; } =  "Actual Chat Admin";
            public static string Picture { get; } = "https://avatars.dicebear.com/api/avataaars/12333323132.svg";
        }

        public static class Walle
        {
            public static UserId UserId { get; } = new("walle", AssumeValid.Option);
            public static long AuthorLocalId { get; } = -1;
            public static string Name { get; } =  "Wall-E";
            public static string Picture { get; } = "https://avatars.dicebear.com/api/bottts/12.svg";
        }

        public static class Claims
        {
            public static string Status { get; } = "urn:actual.chat:status";
        }

        public static IReadOnlySet<UserId> SystemUserIds = new HashSet<UserId>(new [] {Admin.UserId, Walle.UserId});
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
}
