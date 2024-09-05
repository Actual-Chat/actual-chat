using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static HostInfo HostInfo { get; set; } = new();

    public static class DebugMode
    {
        // Rpc calls
        public static class RpcCalls
        {
            public static readonly bool ApiClient = false;
            public static readonly bool ApiServer = false;
            public static readonly bool BackendClient = false;
            public static readonly bool BackendServer = true;
            public static readonly RandomTimeSpan? AnyServerInboundDelay = null; // new(0.5, 0.1);
            public static readonly bool LogExistingCacheEntryUpdates = true; // Used only in WASM
        }

        // Core components
        public static readonly bool SignalR = false;
        public static readonly bool StoredState = false;
        public static readonly bool SyncedState = false;
        public static readonly bool MessageProcessor = false;
        public static readonly bool MarkupParser = false;
        public static readonly bool ServerFusionMonitor = false; // Applies only to dev server
        public static readonly bool DisableStaticFileCaching = false; // Applies only to dev server
        public static readonly bool RemoteComputedCache = false;
        public static readonly bool MeshLocks = false;
        public static readonly bool ShardWorker = false;
        public static readonly bool QueueProcessor = false;

        // UI services
        public static readonly bool History = false;
        public static readonly bool ChatUI = false;

        // UI components
        public static readonly bool ChatListComponents = false;

        // Audio
        public static readonly bool WebMReader = false;
        public static readonly bool AudioSource = false;
        public static readonly bool AudioProcessor = false;
        public static readonly bool AudioPlayback = false;
        public static bool AudioPlaybackPlayMyOwnAudio
            => HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.MauiApp;
        public static readonly bool AudioRecording = false;
        public static readonly bool AudioRecordingStream = false;

        // Transcription
        public static readonly bool Transcription = false;
        public static readonly bool TranscriberAny = false;
        public static readonly bool TranscriberGoogle = false;

        // Search
        public static readonly bool OpenSearchRequest = true;

        // Database
        public static readonly bool Npgsql = false;

        // Misc.
        public static readonly bool KubeEmulation = false;
    }
}
