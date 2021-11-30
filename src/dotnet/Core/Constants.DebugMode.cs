namespace ActualChat;

public static partial class Constants
{
    public static class DebugMode
    {
        public static bool VirtualList { get; } = false;
        public static bool AudioSource { get; } = false;
        public static bool AudioPlayback { get; } = true;
        public static bool AudioPlaybackPlayMyOwnAudio { get; } = true;
        public static bool AudioRecording { get; } = true;
        public static bool AudioRecordingBlobStream { get; } = false;
        public static bool WebMReader { get; } = false;
    }
}
