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
        public static readonly bool ShardOwner = false;
        public static readonly bool QueueProcessor = false;
        public static readonly bool Flows = false;

        // UI services
        public static readonly bool History = false;
        public static readonly bool ChatUI = false;

        // UI components
        public static readonly bool ChatListRelated = false;

        // Audio
        public static readonly bool WebMReader = false;
        public static readonly bool AudioSource = false;
        public static readonly bool AudioProcessor = true;
        public static readonly bool AudioPlayback = false;
        public static readonly bool NativeAudioPlayback = false;

        public static bool AudioPlaybackPlayMyOwnAudio
            => HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.MauiApp;
        public static readonly bool AudioRecording = true;
        public static readonly bool AudioRecordingStream = false;

        // Transcription
        public static readonly bool TranscriberAny = false;
        public static readonly bool TranscriberGoogle = false;
        public static readonly bool TranscriberDeepgram = false;

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
        public const bool ThrottledWorkQueue = false;
    }
}
