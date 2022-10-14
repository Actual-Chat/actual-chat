using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static HostInfo HostInfo { get; set; } = new ();

    public static class DebugMode
    {
        public static bool VirtualList { get; } = false;

        public static bool AudioSource { get; } = false;
        public static bool AudioProcessor { get; } = false;
        public static bool AudioPlayback { get; } = false;
        public static bool AudioPlaybackPlayMyOwnAudio => HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.Maui;
        public static bool AudioRecording { get; } = true;
        public static bool AudioRecordingStream { get; } = false;
        public static bool AudioStreamProxy { get; } = true;

        public static bool Transcription { get; } = false;
        public static bool TranscriberAny { get; } = false;
        public static bool TranscriberGoogle { get; } = false;
        public static bool TranscriptStreamProxy { get; } = true;

        public static bool WebMReader { get; } = false;
        public static bool MarkupParser { get; } = false;

        public static bool SignalR { get; } = false;

        public static bool MarkupEditor { get; } = true;
        public static bool SlateEditor { get; } = false;

        public static bool KubeEmulation { get; } = false;
    }
}
