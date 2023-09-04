using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static class Api
    {
        public static string Version { get; } = "0.8.2-alpha";
    }

    public static class Chat
    {
        public static ChatId DefaultChatId { get; } = new("the-actual-one", default, default, AssumeValid.Option);
        public static ChatId AnnouncementsChatId { get; } = new("announcements", default, default, AssumeValid.Option);
        public static ChatId FeedbackTemplateChatId { get; } = new("feedback-template", default, default, AssumeValid.Option);
        public static IReadOnlySet<Symbol> SystemChatIds { get; } =
            new HashSet<Symbol>(new [] { DefaultChatId.Id, AnnouncementsChatId.Id, FeedbackTemplateChatId.Id });

        public static TileStack<long> IdTileStack { get; } = TileStacks.Long5To1K;
        public static TileStack<Moment> TimeTileStack { get; } = TileStacks.Moment3MTo6Y;
        public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
        public const int MaxSearchFilterLength = 100;
        public const int ReactionFirstAuthorIdsLimit = 10;
        public const int ImageRowCapacity = 4;
    }

    public static class User
    {
        public static class Admin
        {
            public static UserId UserId { get; } = new("actual-admin", AssumeValid.Option);
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

        public static IReadOnlySet<UserId> SystemUserIds = new HashSet<UserId>(new [] { Admin.UserId, Walle.UserId });
        public static int TestBotCount { get; } = 30;
    }

    public static class Team
    {
        public static string EmailSuffix => "@actual.chat";
        public static string Member1Email => "alex.yakunin@actual.chat";
        public static string Member2Email => "alexey.kochetov@actual.chat";
    }

    public static class Attachments
    {
        public const int FileSizeLimit = 100 * 1024 * 1024;
        public const int FileCountLimit = 10;
    }

    public static class Contacts
    {
        public static int MinLoadLimit { get; } = 20;
        public static TimeSpan MinTouchInterval { get; } =  TimeSpan.FromSeconds(10);
    }

    public static class Session
    {
        public static string CookieName { get; } = "FusionAuth.SessionId";
        public static string HeaderName { get; } = "Session";
        public static TimeSpan MinUpdatePresencePeriod { get; } = TimeSpan.FromHours(1);
        public static TimeSpan SessionInfoUpdatePeriod { get; } = TimeSpan.FromHours(1);
    }

    public static class Presence
    {
        public static TimeSpan ActivityPeriod { get; } = TimeSpan.FromSeconds(30);
        public static TimeSpan CheckPeriod { get; } = TimeSpan.FromSeconds(10);
        public static TimeSpan CheckInPeriod { get; } = TimeSpan.FromSeconds(49);
        public static TimeSpan CheckInClientConnectTimeout { get; } = TimeSpan.FromSeconds(10);
        public static TimeSpan CheckInRetryDelay { get; } = TimeSpan.FromSeconds(15);
        public static TimeSpan AwayTimeout { get; } = TimeSpan.FromSeconds(60);
        public static TimeSpan OfflineTimeout { get; } = TimeSpan.FromMinutes(10);
    }

    // Diagnostics, etc.

    public static class Sentry
    {
        public static HashSet<AppKind> EnabledFor { get; } = new () {AppKind.MauiApp};
    }

    public static class Auth
    {
        public static readonly string[] EmailSchemes = { Google.SchemeName, Apple.SchemeName };
        public static class Phone
        {
            public const string SchemeName = "phone";
            public const string CallbackPath = "/signin/phone/callback";
            public const int TotpLength = 6;
        }
        public static class Email
        {
            public const string SchemeName = "email"; // used only as identity name for now
        }
        public static class Google
        {
            public const string SchemeName = "Google";
        }
        public static class Apple
        {
            public const string SchemeName = "Apple";
        }
    }
}
