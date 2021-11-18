namespace ActualChat;

public static partial class Constants
{
    public static class DebugMode
    {
        public static bool VirtualList { get; } = true;
        public static bool AudioTrackPlayer { get; } = true;
        public static bool AudioRecorder { get; } = true;
        public static bool WebMReader { get; } = false;

        public static class SourceAudio
        {
            public static bool DumpBlobParts { get; } = false;
        }
    }
}
