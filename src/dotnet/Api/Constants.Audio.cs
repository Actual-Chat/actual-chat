namespace ActualChat;

public static partial class Constants
{
    public static class Audio
    {
        public const int StreamingChannelCapacity = 1024;
        public const int OpusFrameDurationMs = 20;
        public const int VadFrameDurationMs = 32;
        public const int ApmFrameDurationMs = 10;
        public const int OpusFrameLength = RecordingSampleRate / 1000 * OpusFrameDurationMs;
        public const int VadFrameLength = RecordingSampleRate / 1000 * VadFrameDurationMs;
        public const int PcmFrameLength = PlaybackSampleRate / 1000 * OpusFrameDurationMs;
        public const int Bitrate = 32000;
        public const int Channels = 1;
        public const int RecordingSampleRate = 16000;
        public const int PlaybackSampleRate = 48000;
        public static readonly TimeSpan OpusFrameDuration = TimeSpan.FromMilliseconds(OpusFrameDurationMs);
        public static readonly TimeSpan ListeningDuration = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan RecordingDuration = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxRealtimeStreamDrift = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan MaxStreamDuration = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan MaxBeginsAtDrift = TimeSpan.FromSeconds(5);

        // Watchdog: cancel ProcessAudio handler if no frame arrives within this window.
        // Opus frames are 20 ms; 2 s of silence means the producer is pathologically stalled.
        public static readonly TimeSpan FrameSilenceTimeout = TimeSpan.FromSeconds(2);

        // RPC stream flow control for audio (50fps, 20ms frames).
        // Tuned for up to ~1s RTT: bufferSize > ackPeriod + fps × RTT.
        public const int StreamAckPeriod = 64;
        public const int StreamBufferSize = 192;
        public static readonly TimeSpan LowPlaybackBufferDuration = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan StartPlaybackWhenBufferedDuration = TimeSpan.FromSeconds(0.1);
    }
}
