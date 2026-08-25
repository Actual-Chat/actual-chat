using System.Numerics;

namespace ActualChat;

public static partial class Constants
{
    public static class Hosts
    {
        public const string Voxt = CoreConstants.Hosts.Prod;
        public const string DevVoxt = CoreConstants.Hosts.Dev;
        public const string LocalVoxt = CoreConstants.Hosts.Local;

        // Suffix for wildcard matching (used for worktree subdomains like wt1.local.voxt.ai)
        public const string LocalVoxtSuffix = ".local.voxt.ai";

        public static readonly IReadOnlySet<string> AltProd = new HashSet<string>(["actual.chat"], StringComparer.OrdinalIgnoreCase);
        public static readonly IReadOnlySet<string> AltDev = new HashSet<string>(["dev.actual.chat"], StringComparer.OrdinalIgnoreCase);
        public static readonly IReadOnlySet<string> AltLocal = new HashSet<string>(["local.actual.chat"], StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlySet<string> AllProd = new HashSet<string>([Voxt, ..AltProd], StringComparer.OrdinalIgnoreCase);
        public static readonly IReadOnlySet<string> AllDev = new HashSet<string>([DevVoxt, ..AltDev], StringComparer.OrdinalIgnoreCase);
        public static readonly IReadOnlySet<string> AllLocal = new HashSet<string>([LocalVoxt, ..AltLocal], StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Checks if a host is a local development host (including worktree subdomains).
        /// Matches: local.voxt.ai, local.actual.chat, wt1.local.voxt.ai, cdn-wt1.local.voxt.ai, etc.
        /// </summary>
        public static bool IsLocalDev(string host)
            => AllLocal.Contains(host)
            || host.EndsWith(LocalVoxtSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static class Origins
    {
        // Origins of MAUI's BlazorWebView host page - unroutable, so unclaimable by an attacker.
        public static readonly IReadOnlyList<string> AppWebView = [
            "http://0.0.0.0",
            "https://0.0.0.0",
            "app://0.0.0.0",
            "http://0.0.0.1",
            "https://0.0.0.1",
            "app://0.0.0.1",
        ];
    }

    public static class Session
    {
        public static readonly string CookieName = "FusionAuth.SessionId";
        public static readonly string TokenCookieName = "FusionAuth.SessionToken";
        public static readonly string HeaderName = "Session";
        public static readonly string QueryParameterName = "session";
        public static readonly TimeSpan LastSeenAtUpdatePeriod = TimeSpan.FromMinutes(30);
    }

    public static class Place
    {
        public static readonly IReadOnlySet<Symbol> SystemPlaceIds = new HashSet<Symbol>();
        public static readonly HashSet<string> SystemPlaceIdValues = SystemPlaceIds.Select(x => x.Value).ToHashSet();
    }

    public static class Chat
    {
        public static readonly GroupChatId DefaultChatId = GroupChatId.Parse("the-actual-one");
        public static readonly GroupChatId AnnouncementsChatId = GroupChatId.Parse("announcements");
        public static readonly GroupChatId FeedbackTemplateChatId = GroupChatId.Parse("feedback-template");
        public static readonly HashSet<ChatId> SystemChatIds = [DefaultChatId, AnnouncementsChatId, FeedbackTemplateChatId];
        public static readonly HashSet<string> SystemChatIdValues = SystemChatIds.Select(x => x.Value).ToHashSet();

        public static readonly TileStack<long> ServerIdTileStack = TileStacks.Long5To1K;
        public static readonly TileStack<long> ReaderIdTileStack = TileStacks.Long5To80;
        public static readonly TileStack<long> ViewIdTileStack = TileStacks.Long5To20;
        public static readonly TileStack<int> ChatTileStack = TileStacks.Int5To20;
        public static readonly TimeSpan MaxEntryDuration = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan StreamingEntryFixupDelay = MaxEntryDuration + TimeSpan.FromSeconds(30);
        // How long after the last interaction we still count an open chat at its tail as "being read".
        // Much longer than Presence.ActivityPeriod: reading a long message without touching anything
        // is normal, while a user who walked away must stop consuming unread messages.
        public static readonly TimeSpan ReadingGracePeriod = TimeSpan.FromMinutes(3);
        // 2x the editor's large-paste threshold, which is per paste rather than per message
        public const int MaxEntryTextLength = 64 * 1024;
        public const int NonContactPeerMessageLimit = 2;
        public const int ReactionFirstAuthorIdsLimit = 10;
        public const int MaxSearchFilterLength = 100;
        public const int MinChatPageMapSize = 100;

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
                private static readonly Symbol[] AllowMultiplePerUser = [Bot, Welcome];
                public static bool MustBeUniquePerUser(Symbol systemTag)
                    => AllowMultiplePerUser.All(e => e != systemTag);
            }
        }
    }

    public static class User
    {
        public static class Claims
        {
            public const string GooglePicture = "google/picture";
        }

        public static class Admin
        {
            public static readonly UserId UserId = UserId.Parse("VoxtAdmin"); // Should have no dash in it!
            public static readonly string Name =  $"{CoreConstants.AppName} Admin";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12333323132";
        }

        public static class Walle
        {
            public static readonly UserId UserId = UserId.Parse("walle");
            public static readonly long AuthorLocalId = -1;
            public static readonly string Name =  "Wall-E";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12";
        }

        public static class Sherlock
        {
            public static readonly UserId UserId = UserId.Parse("sherlock");
            public static readonly long AuthorLocalId = -2;
            public static readonly string Name =  "AI Search Bot";
            public static readonly MediaId MediaId = MediaId.Parse("system-icons:sherlock");

            public static AuthorId GetSherlockAuthorId(ChatId chatId)
                => AuthorId.New(chatId, AuthorLocalId);
        }

        public static readonly IReadOnlyList<UserId> SystemUserIds = [Admin.UserId, Walle.UserId, Sherlock.UserId];
        public static readonly IReadOnlyList<string> SystemUserIdValues = SystemUserIds.Select(x => x.Value).ToArray();
        public static readonly int TestBotCount = 30;
    }

    public static class Invites
    {
        public static class Defaults
        {
            public static readonly int ChatRemaining = 10_000;
            public static readonly int PlaceRemaining = 10_000;
            public static readonly TimeSpan ExpiresIn = TimeSpan.FromDays(30);
        }
    }

    public static class Team
    {
        public const string EmailDomain = "actual.chat"; // NOTE: !!! used for admin detection!!!
        public const string EmailSuffix = $"@{EmailDomain}";
        public static string SupportEmail => $"support{EmailSuffix}";
        public static string Member1Email => $"alex.yakunin{EmailSuffix}";
        public static string Member2Email => $"alexey.kochetov{EmailSuffix}";
        public static string CopyrightAgentEmail => $"dmca{EmailSuffix}";
    }

    public static class Attachments
    {
        public const int FileSizeLimit = 500 * 1024 * 1024;
        public const int AvatarPictureFileSizeLimit = 50 * 1024 * 1024;
        public const int FileCountLimit = 10;

        /// <summary>HTML accept attribute value for avatar picture file inputs.</summary>
        public static readonly string AvatarPictureAccept = string.Join(',',
            MediaTypeExt.SupportedAvatarContentTypes
                .Select(m => MediaMimeTypes.TryGetExtension(m, out var ext) ? ext : null)
                .SkipNullItems());
        public const int MaxImageWidth = 480; // In pixels
        public const int MaxImageHeight = 360; // In pixels
        public const int MaxThumbnailWidth = 48; // In pixels
        public const int MaxThumbnailHeight = 36; // In pixels
        public const int MaxAvatarPreviewSize = 720; // Preview size requested when rendering avatars; stored size is capped by MaxIconSize
        public const int MaxIconSize = 1024;
        public static readonly Vector2 MaxResolution = new(MaxImageWidth, MaxImageHeight);
        public static readonly Vector2 MaxActualResolution = MaxResolution * 2;
        public static readonly Vector2 MaxThumbnailResolution = new(MaxThumbnailWidth, MaxThumbnailHeight);
        public static readonly Vector2 MaxActualThumbnailResolution = MaxThumbnailResolution * 2;
    }

    public static class Contacts
    {
        public static readonly int MinLoadLimit = 20;
        public static readonly TimeSpan MinTouchInterval =  TimeSpan.FromSeconds(10);
        public static readonly TimeSpan PermissionRequestDismissPeriod = TimeSpan.FromDays(7);
    }

    public static class Presence
    {
        public static readonly TimeSpan ActivityPeriod = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan CheckPeriod = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CheckInClientConnectTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CheckInRetryDelay = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan AwayTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan OfflineTimeout = TimeSpan.FromMinutes(10);
    }

    public static class Location
    {
        public static readonly TimeSpan UpdatePeriod = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan GetTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan UnlimitedDuration = TimeSpan.MaxValue;
        public static readonly IReadOnlyDictionary<TimeSpan, string> Durations = new Dictionary<TimeSpan, string> {
            [TimeSpan.FromMinutes(15)] = "for 15 minutes",
            [TimeSpan.FromHours(1)] = "for 1 hour",
            [TimeSpan.FromHours(8)] = "for 8 hours",
            [UnlimitedDuration] = "Until I turn it off",
        };
        public const int MaxSharingAuthorsPerChat = 100;
    }

    public static class Messages
    {
        public static readonly string RecordingSkeleton = "🎙…";
    }

    public static class Transcription
    {
        public static readonly TimeSpan ThrottlePeriod = TimeSpan.FromSeconds(0.2);
        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan RetranscriptionTimeout = TimeSpan.FromSeconds(20);
        // Max time the realtime transcriber gets to complete after its audio source ends
        public static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);
        public static readonly bool StartWithEllipsis = false;
        public static readonly bool IsRetranscriptionEnabled = true;
        // A token can't be sent until its Ogg page is complete, so this is pure added lag on every
        // streaming provider. Measured against Soniox: 200ms -> ~490ms leading-edge lag, 60ms ->
        // ~320ms, 20ms -> ~305ms for 12% more bytes. Offline paths keep the larger default.
        public static readonly TimeSpan StreamPageDuration = TimeSpan.FromMilliseconds(60);
        // The bar a message clears before a second pass is worth its full extra transcription.
        // Either one is enough: a wordy short message is worth refining, and so is a long one the
        // realtime pass produced few words for.
        public static readonly int MinRetranscriptionWords = 10;
        public static readonly TimeSpan MinRetranscriptionDuration = TimeSpan.FromSeconds(5);

        public static class Google
        {
            // Tied to CoreSettings:GoogleRegionId, and part of the recognizer id so a switch
            // provisions a fresh recognizer: "long" has no ru-RU outside the "us" multi-region.
            public static readonly string Model = "chirp_2";
            public static readonly bool IsWebMOpusEnabled = false;
            public static readonly bool UseStabilityHeuristics = true;
            public static readonly TimeSpan SilentPrefixDuration = TimeSpan.Zero;
            public static readonly TimeSpan SilentSuffixDuration = TimeSpan.Zero; // TimeSpan.FromSeconds(4);
            public static readonly double Speed = 2;
        }

        public static class Deepgram
        {
            public static readonly double Speed = 2;
            public static readonly TimeSpan SilentPrefixDuration = TimeSpan.FromSeconds(0);
            public static readonly TimeSpan SilentSuffixDuration = TimeSpan.FromSeconds(1);
        }

        public static class Soniox
        {
            public static readonly double Speed = 2;
            // The server drops the connection after 20s without audio or a keepalive.
            public static readonly TimeSpan KeepAlivePeriod = TimeSpan.FromSeconds(5);
            // Both govern finalization, not token latency - tokens stream either way - so they
            // only trade how often the tail stops being rewritten.
            public static readonly int MaxEndpointDelayMs = 2000; // 500...3000, default = 2000
            public static readonly double EndpointSensitivity = 0.0; // -1.0...1.0, default = 0.0
            // The end-of-audio frame makes the server finalize everything, so no silent padding
            // is needed to flush the tail - and padding is billed as stream time.
            public static readonly TimeSpan SilentPrefixDuration = TimeSpan.Zero;
            public static readonly TimeSpan SilentSuffixDuration = TimeSpan.Zero;
        }

        public static class ElevenLabs
        {
            public static readonly double Speed = 2;
            public static readonly TimeSpan SilentPrefixDuration = TimeSpan.Zero;
            public static readonly TimeSpan SilentSuffixDuration = TimeSpan.Zero;
            // VAD commits a segment once speech stops; manual commits would have to guess where.
            public static readonly double VadThreshold = 0.5;
            public static readonly double VadSilenceThresholdSeconds = 0.5;
        }
    }

    public static class Recaptcha
    {
        public static class Actions
        {
            public static readonly string PhoneSignIn = nameof(PhoneSignIn);
            public static readonly string PhoneVerify = nameof(PhoneVerify);
            public static readonly string EmailSignIn = nameof(EmailSignIn);
            public static readonly string EmailVerify = nameof(EmailVerify);

            public static string ForPurpose(TotpPurpose purpose)
                => purpose switch {
                    TotpPurpose.SignInPhone => PhoneSignIn,
                    TotpPurpose.VerifyPhone => PhoneVerify,
                    TotpPurpose.SignInEmail => EmailSignIn,
                    TotpPurpose.VerifyEmail => EmailVerify,
                    _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
                };
        }

        public static readonly float ValidScore = 0.5f;
    }

    public static class Auth
    {
        public static class Phone
        {
            public const int TotpLength = 6;
        }

        public static class Email
        {
            public const int TotpLength = 6;
        }

        public static class TestAgent
        {
            public const string EmailPrefix = "test-";
            public const string EmailDomain = "@actual.chat";

            public static bool IsTestAgentEmail(string email)
                => email.StartsWith(EmailPrefix, StringComparison.OrdinalIgnoreCase)
                    && email.EndsWith(EmailDomain, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class Call
    {
        // How long a call rings an unanswered invitee before it's marked Missed and the ring stops.
        // Owned by LiveSessionsBackend; shared so the notification expiry and the Android banner's
        // self-destruct timeout can't drift from it.
        // TODO: revert to 40s - lowered to 20s only for call-status testing.
        public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(20);
        // Redis field TTL on a ringing invite: the no-observer backstop that expires a stale ring
        // even if the caller disconnects and nobody polls GetState. Longer than RingTimeout so the
        // observed path marks it Missed first; a status change rewrites the field without this TTL.
        public static readonly TimeSpan RingTtl = TimeSpan.FromSeconds(60);
    }

    public static class Notification
    {
        public const string CallTagPrefix = "call-";

        public static class MessageDataKeys
        {
            public const string NotificationId = "notificationId";
            public const string AuthorId = "authorId";
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
            public const string Messages = "messages";
            public const string Timestamp = "timestamp";
            public const string DismissedIds = "dismissedIds";
            public const string DismissedTags = "dismissedTags";
            public const string Silent = "silent";

            public static readonly string[] ValidKeys = {
                AuthorId, Body, ChatId, ChatEntryId, DismissedIds, DismissedTags, LastEntryLocalId, Icon, ImageUrl, Kind, Link, Messages, NotificationId, Silent, Tag, Title, Timestamp
            };

            public static bool IsValidKey(string key)
                => ValidKeys.Contains(key);
        }

        public static class ThrottleIntervals
        {
            public static readonly TimeSpan Message = TimeSpan.FromSeconds(30);
        }

        // Audible-alert back-off for a coalesced chat notification, indexed by the number of alerts
        // already emitted (index 0 is unused — the first alert always fires). Later values clamp to
        // the last entry: alert immediately, then ~10s a few times, then every 5min, then every 30min.
        public static readonly TimeSpan[] BeepBackoff = {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
        };
        // After this much silence the beep back-off resets, so the next message alerts immediately
        // (a fresh burst should be reactable ASAP rather than inheriting the previous burst's back-off).
        public static readonly TimeSpan BeepResetPeriod = TimeSpan.FromMinutes(5);
        // Spoken messages alert on a change of "voice context" - the speaker - and then no more
        // often than this. A monologue is one conversation to the listener, not one alert per
        // utterance.
        public static readonly TimeSpan VoiceReAlertInterval = TimeSpan.FromMinutes(10);
        // Unread mentions re-alert on this fixed interval (they never coalesce with chat messages),
        // at most MaxMentionReAlerts times per mention.
        public static readonly TimeSpan MentionReAlertInterval = TimeSpan.FromMinutes(10);
        public const int MaxMentionReAlerts = 2;
        // Messages kept verbatim in a coalesced notification (its transcript window); older ones
        // fold into the "+N earlier messages" tail.
        public const int MaxRecentMessages = 5;
        public const int MaxRecentMessageTextLength = 200;
        // Distinct author names shown in a coalesced notification's summary before "+N more".
        public const int MaxSummaryAuthors = 3;
        // Distinct authors tracked on a coalesced notification (bounds the stored set).
        public const int MaxTrackedAuthors = 8;

        public static readonly TimeSpan PermissionRequestDismissPeriod = TimeSpan.FromDays(7);
        public static readonly TimeSpan EntryWaitTimeout = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan ActiveDevicePeriod = TimeSpan.FromDays(30);

        // A new push is sent right away only if the previous one is older than this;
        // otherwise similar low-urgency notifications coalesce into a deferred tick.
        public static readonly TimeSpan SilencePeriod = TimeSpan.FromSeconds(10);
        // Once a user accumulates this many displayed notifications without engaging,
        // the backend goes dormant and stops all work until an engagement signal.
        public const int DormancyThreshold = 100;
        // A ring that outlives the ring itself by this margin was never dismissed: its cancel
        // command was lost, or the caller vanished before anything observed the ring.
        public static readonly TimeSpan RingExpirationMargin = TimeSpan.FromSeconds(5);
        // A reaction clears when its entry is seen (NotificationDismissMode.OnView); this is the
        // backstop for a chat the user never reopens. Smiles aren't worth keeping around longer.
        public static readonly TimeSpan ReactionLifespan = TimeSpan.FromDays(1);
        // Read-position advances are frequent (~1/s while scrolling); the read-reconcile event
        // is collapsed to one per (user, chat) per this window via a delay-bucketed event uuid.
        // This bounds the background-banner dismissal lag on other devices (in-app is instant).
        public static readonly TimeSpan ReadReconcileWindow = TimeSpan.FromSeconds(15);
        // When a present user is already caught up to a chat's head, a new plain message is held
        // (not pushed) for this grace period instead of alerting immediately. If the user reads it
        // within the window it's dropped silently on every device; otherwise it alerts as usual.
        public static readonly TimeSpan ActiveReaderGrace = TimeSpan.FromSeconds(10);
    }

    public static class Media
    {
        public const int LinkPreviewsPerMessageLimit = 20;
    }

    public static class Search
    {
        public const int PageSizeLimit = 50;
        public const int DefaultPageSize = 5;
        public const int ExtendedPageSize = 30;
    }

    public static class Sentry
    {
        public static readonly HashSet<HostKind> EnabledFor = [HostKind.MauiApp];
    }

    public static class Translation
    {
        public const string ServiceKey = nameof(Translation);
        public const string RealtimeServiceKey = $"{nameof(Translation)}Realtime";
        public const string UITextServiceKey = $"{nameof(Translation)}UIText";
        public const string NoTranslationNeededText = "NO_TRANSLATION_NEEDED";
        public const int MaxTextTranslationLength = 1024;
    }

    public static class LanguageDetection
    {
        public const string ServiceKey = nameof(LanguageDetection);
    }
}
