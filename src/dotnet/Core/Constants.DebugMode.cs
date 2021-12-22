using ActualChat.Hosting;

namespace ActualChat;

public static partial class Constants
{
    public static HostInfo HostInfo { get; set; } = new();

    public static class DebugMode
    {
        public static bool VirtualList { get; } = false;
        public static bool AudioSource { get; } = false;
        public static bool AudioProcessing { get; } = false;
        public static bool AudioPlayback { get; } = false;
        public static bool AudioPlaybackPlayMyOwnAudio => HostInfo.IsDevelopmentInstance;
        public static bool AudioRecording { get; } = true;
        public static bool AudioRecordingStream { get; } = true;
        public static bool Transcription { get; } = false;
        public static bool GoogleTranscriber { get; } = false;
        public static bool WebMReader { get; } = false;
    }
}
