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
        public static bool AudioRecording { get; } = false;
        public static bool AudioRecordingBlobStream { get; } = false;
        public static bool Transcription { get; } = true;
        public static bool GoogleTranscriber { get; } = true;
        public static bool WebMReader { get; } = false;
    }
}
