using System.Numerics;
using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static class Api
    {
        public static readonly string StringVersion = ThisAssembly.AssemblyVersion; // X.Y.0.0
        public static readonly Version Version = Version.Parse(StringVersion);

        public static class Compression
        {
            public const bool IsServerSideEnabled = true;
            public const bool IsClientSideEnabled = true;
        }
    }

    public static class Hosts
    {
        public const string ActualChat = "actual.chat";
        public const string DevActualChat = "dev.actual.chat";
        public const string LocalActualChat = "local.actual.chat";
    }

    public static class Chat
    {
        public static readonly ChatId DefaultChatId = ChatId.Group("the-actual-one");
        public static readonly ChatId AnnouncementsChatId = ChatId.Group("announcements");
        public static readonly ChatId FeedbackTemplateChatId = ChatId.Group("feedback-template");
        public static readonly IReadOnlySet<Symbol> SystemChatIds =
            new HashSet<Symbol>(new [] { DefaultChatId.Id, AnnouncementsChatId.Id, FeedbackTemplateChatId.Id });
        public static readonly string[] SystemChatSids = SystemChatIds.Select(x => x.Value).ToArray();

        public static readonly TileStack<long> ServerIdTileStack = TileStacks.Long5To1K;
        public static readonly TileStack<long> ReaderIdTileStack = TileStacks.Long5To80;
        public static readonly TileStack<long> ViewIdTileStack = TileStacks.Long5To20;
        public static readonly TileStack<int> ChatTileStack = TileStacks.Int5To20;
        public static readonly TileStack<Moment> TimeTileStack = TileStacks.Moment3MTo6Y;
        public static readonly TimeSpan MaxEntryDuration = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
        public const int MaxSearchFilterLength = 100;
        public const int ReactionFirstAuthorIdsLimit = 10;
        public const int ImageRowCapacity = 4;

        public static class SystemTags
        {
            public static readonly Symbol Notes = "notes";
            // Not used!
            public static readonly Symbol Family = "family";
            // Not used!
            public static readonly Symbol Friends = "friends";
            // Not used!
            public static readonly Symbol ClassmatesAlumni = "classmates-alumni";
            // Not used!
            public static readonly Symbol Coworkers = "coworkers";
            public static readonly Symbol Welcome = "welcome";
            public static readonly Symbol Bot = "ml-bot";
            public static class Rules {
                private static readonly Symbol[] allowMultiplePerUser = new []{ SystemTags.Bot, SystemTags.Welcome };
                public static bool MustBeUniquePerUser(Symbol systemTag) => !allowMultiplePerUser.Any(e => e == systemTag);
            }
        }
    }

    public static class User
    {
        public static class Admin
        {
            public static readonly UserId UserId = new("actual-admin", AssumeValid.Option);
            public static readonly string Name =  "Actual Chat Admin";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12333323132";
        }

        public static class Walle
        {
            public static readonly UserId UserId = new("walle", AssumeValid.Option);
            public static readonly long AuthorLocalId = -1;
            public static readonly string Name =  "Wall-E";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12";
        }

        public static class MLSearchBot
        {
            public static readonly UserId UserId = new("ml-search", AssumeValid.Option);
            public static readonly long AuthorLocalId = -2;
            public static readonly string Name =  "AI Search Bot";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12";
        }

        public static readonly IReadOnlyList<UserId> SystemUserIds = [Admin.UserId, Walle.UserId, MLSearchBot.UserId];
        public static readonly IReadOnlyList<string> SSystemUserIds = SystemUserIds.Select(x => x.Value).ToArray();
        public static readonly int TestBotCount = 30;
    }

    public static class Invites
    {
        public static class Defaults
        {
            public static readonly int ChatRemaining = 10_000;
            public static readonly int PlaceRemaining = 10_000;
            public static readonly int UserRemaining = 10;
            public static readonly TimeSpan ExpiresIn = TimeSpan.FromDays(30);
        }
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
        public const int AvatarPictureFileSizeLimit = 50 * 1024 * 1024;
        public const int FileCountLimit = 10;
        public const int MaxImageWidth = 480; // In pixels
        public const int MaxImageHeight = 360; // In pixels
        public static readonly Vector2 MaxResolution = new(MaxImageWidth, MaxImageHeight);
        public static readonly Vector2 MaxActualResolution = MaxResolution * 2;
    }

    public static class Contacts
    {
        public static readonly int MinLoadLimit = 20;
        public static readonly TimeSpan MinTouchInterval =  TimeSpan.FromSeconds(10);
        public static readonly TimeSpan PermissionRequestDismissPeriod = TimeSpan.FromDays(7);
    }

    public static class Session
    {
        public static readonly string CookieName = "FusionAuth.SessionId";
        public static readonly string HeaderName = "Session";
        public static readonly TimeSpan MinUpdatePresencePeriod = TimeSpan.FromHours(1);
        public static readonly TimeSpan SessionInfoUpdatePeriod = TimeSpan.FromHours(1);
    }

    public static class Presence
    {
        public static readonly TimeSpan ActivityPeriod = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan CheckPeriod = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CheckInPeriod = TimeSpan.FromSeconds(49);
        public static readonly TimeSpan CheckInClientConnectTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CheckInRetryDelay = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan AwayTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan OfflineTimeout = TimeSpan.FromMinutes(10);
    }

    public static class Messages
    {
        public static readonly string RecordingSkeleton = "🎙…";
    }

    public static class Transcription
    {
        public static readonly bool StartWithEllipsis = true;

        public static class Google
        {
            public static readonly bool IsWebMOpusEnabled = false;
            public static readonly bool UseStabilityHeuristics = true;
            public static readonly TimeSpan SilentPrefixDuration = TimeSpan.FromSeconds(3);
            public static readonly TimeSpan SilentSuffixDuration = TimeSpan.Zero; // TimeSpan.FromSeconds(4);
            public static readonly double Speed = 2;
        }

        public static class Deepgram
        {
            public static readonly double Speed = 2;
        }
    }

    // Diagnostics, etc.

    public static class Sentry
    {
        public static readonly HashSet<HostKind> EnabledFor = [HostKind.MauiApp];
    }

    public static class Auth
    {
        public static class Phone
        {
            public const string CallbackPath = "/signin/phone/callback";
            public const int TotpLength = 6;
        }

        public static class Email
        {
            public const int TotpLength = 6;
        }
    }

    public static class Notification
    {
        public static class MessageDataKeys
        {
            public const string NotificationId = "notificationId";
            public const string ChatId = "chatId";
            public const string ChatEntryId = "chatEntryId";
            public const string LastEntryLocalId = "lastEntryLocalId";
            public const string Icon = "icon";
            public const string Kind = "kind";
            public const string Link = "link";
            public const string Tag = "tag";
            public const string Title = "title";
            public const string Body = "body";
            public const string ImageUrl = "imageUrl";
            public const string Timestamp = "timestamp";

            public static readonly string[] ValidKeys = {
                Body, ChatId, ChatEntryId, LastEntryLocalId, Icon, ImageUrl, Kind, Link, NotificationId, Tag, Title, Timestamp
            };

            public static bool IsValidKey(string key)
                => ValidKeys.Contains(key, StringComparer.Ordinal);
        }

        public static class ThrottleIntervals
        {
            public static readonly TimeSpan Message = TimeSpan.FromSeconds(30);
        }

        public static readonly TimeSpan PermissionRequestDismissPeriod = TimeSpan.FromDays(7);
        public static readonly TimeSpan EntryWaitTimeout = TimeSpan.FromSeconds(0.5);
    }

    public static class Audio
    {
        public const int OpusFrameDurationMs = 20;
        public static readonly TimeSpan OpusFrameDuration = TimeSpan.FromMilliseconds(OpusFrameDurationMs);
        public static readonly TimeSpan ListeningDuration = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan RecordingDuration = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxRealtimeStreamDrift = TimeSpan.FromSeconds(3);
    }

    public static class Search
    {
        public const int PageSizeLimit = 50;
        public const int ContactSearchDefaultPageSize = 3;
        public const int ContactSearchExtendedPageSize = 30;
    }

    public static class Preferences
    {
        public const string EnableDataCollectionKey = "analytics"; // TODO(AppRename): rename to proper name
    }

    public static class ServerSettings
    {
        public const string UseChatContentArranger2ChatIds = "UseChatContentArranger2ChatIds";
    }
}
