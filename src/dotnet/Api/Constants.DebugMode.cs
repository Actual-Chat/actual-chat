using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static HostInfo HostInfo { get; set; } = new();

    public static class DebugMode
    {
        public static readonly bool LogAnyThrownException = true; // Used only in App + DEBUG

        // Core components
        public static readonly bool MarkupParser = false;
        public static readonly bool ServerFusionMonitor = false; // Applies only to dev server
        public static readonly bool DisableStaticFileCaching = false; // Applies only to dev server
        public static readonly bool RemoteComputedCache = false;
        public static readonly bool WebKvasBackend = false;
        public static readonly bool MeshLocks = false;
        public static readonly bool ShardOwners = false;
        public static readonly bool QueueProcessors = false;
        public static readonly bool Flows = false;

        // UI services
        public static readonly bool History = false;
        public static readonly bool ChatUI = false;

        // UI components
        public static readonly bool ChatListRelated = false;

        // Audio
        public static readonly bool WebMReader = false;
        public static readonly bool AudioSource = false;

        // Video
        public static readonly bool VideoSource = false;
        public static readonly bool VideoPlayback = false;
        public static readonly bool AudioProcessor = false;
        public static readonly bool AudioTrackPlayer = false;
        public static readonly bool NativeAudioPlayer = false;
        public static readonly bool LiveStreaming = false;
        public static readonly bool Tunes = false;

        public static readonly bool ChatAudioUI = false;
        public static readonly bool AudioRecording = false;
        public static readonly bool AudioRecordingStream = false;
        public static bool ListenOwnAudio => HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.MauiApp;

        // Transcription
        public static readonly bool TranscriberAny = false;
        public static readonly bool TranscriberGoogle = false;
        public static readonly bool TranscriberDeepgram = false;

        // Notifications
        public static readonly bool Notifications = false;

        // Translation
        public static readonly bool TranscriptionTranslation = false;
        public static readonly bool TranslationBackend = false;

        // Search
        public static readonly bool OpenSearchRequestResponse = false;

        // Link previews
        public static readonly bool Crawler = false;
        public static readonly bool RobotsFiles = false;

        // Database
        public static readonly bool Npgsql = false;

        // Misc.
        public static readonly bool KubeEmulation = false;
        public static readonly bool KubeLocal = false;
        public const bool ShareSuggestions = false;

        // File upload
        public const bool FileAttachments = false;
        public const bool VideoTranscoding = false;
    }
}
