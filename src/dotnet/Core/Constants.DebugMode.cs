using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static HostInfo HostInfo { get; set; } = new ();

    public static class DebugMode
    {
        // Core components
        public static bool SignalR { get; } = false;
        public static bool StoredState { get; } = false;
        public static bool SyncedState { get; } = false;
        public static bool MarkupParser { get; } = false;
        public static bool ServerFusionMonitor { get; } = false; // Applies only to dev server

        // UI services
        public static bool VirtualList { get; } = false;
        public static bool History { get; } = true;
        public static bool ChatUI { get; } = true;

        // Audio
        public static bool WebMReader { get; } = false;
        public static bool AudioSource { get; } = false;
        public static bool AudioProcessor { get; } = true;
        public static bool AudioPlayback { get; } = false;
        public static bool AudioPlaybackPlayMyOwnAudio => HostInfo.IsDevelopmentInstance && HostInfo.AppKind != AppKind.MauiApp;
        public static bool AudioRecording { get; } = true;
        public static bool AudioRecordingStream { get; } = false;
        public static bool AudioStreamProxy { get; } = true;

        // Transcription
        public static bool Transcription { get; } = false;
        public static bool TranscriberAny { get; } = false;
        public static bool TranscriberGoogle { get; } = true;
        public static bool TranscriptStreamProxy { get; } = false;

        // Misc.
        public static bool KubeEmulation { get; } = false;
    }
}
