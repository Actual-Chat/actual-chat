namespace ActualChat;

partial record AppConstants
{
    /// <summary>
    /// JS-interop snapshot of <see cref="Constants.Audio"/>.
    /// Serialized to TS via BrowserInit and exposed there as the <c>AUDIO</c> field.
    /// </summary>
    public sealed record AudioConstants
    {
        // Frame cadence
        public int OpusFrameDurationMs { get; init; } = Constants.Audio.OpusFrameDurationMs;
        public int VadFrameDurationMs { get; init; } = Constants.Audio.VadFrameDurationMs;
        public int ApmFrameDurationMs { get; init; } = Constants.Audio.ApmFrameDurationMs;
        // Sample rates
        public int RecordingSampleRate { get; init; } = Constants.Audio.RecordingSampleRate;
        public int PlaybackSampleRate { get; init; } = Constants.Audio.PlaybackSampleRate;
        // Frame lengths (samples)
        public int OpusFrameLength { get; init; } = Constants.Audio.OpusFrameLength;
        public int VadFrameLength { get; init; } = Constants.Audio.VadFrameLength;
        public int PcmFrameLength { get; init; } = Constants.Audio.PcmFrameLength;
        // Codec
        public int Bitrate { get; init; } = Constants.Audio.Bitrate;
        public int Channels { get; init; } = Constants.Audio.Channels;
        // Server channel buffering
        public int StreamingChannelCapacity { get; init; } = Constants.Audio.StreamingChannelCapacity;
        // Stream lifecycle / watchdogs
        public double FrameSilenceTimeoutMs { get; init; } = Constants.Audio.FrameSilenceTimeout.TotalMilliseconds;
        public double MaxRealtimeStreamDriftMs { get; init; } = Constants.Audio.MaxRealtimeStreamDrift.TotalMilliseconds;
        public double MaxBeginsAtDriftMs { get; init; } = Constants.Audio.MaxBeginsAtDrift.TotalMilliseconds;
        public double MaxStreamDurationMs { get; init; } = Constants.Audio.MaxStreamDuration.TotalMilliseconds;
        public double ListeningDurationMs { get; init; } = Constants.Audio.ListeningDuration.TotalMilliseconds;
        public double RecordingDurationMs { get; init; } = Constants.Audio.RecordingDuration.TotalMilliseconds;
        // RPC stream flow control
        public int StreamAckPeriod { get; init; } = Constants.Audio.StreamAckPeriod;
        public int StreamBufferSize { get; init; } = Constants.Audio.StreamBufferSize;
        // Playback buffer
        public double LowPlaybackBufferDurationMs { get; init; } = Constants.Audio.LowPlaybackBufferDuration.TotalMilliseconds;
        public double StartPlaybackWhenBufferedDurationMs { get; init; } = Constants.Audio.StartPlaybackWhenBufferedDuration.TotalMilliseconds;
    }
}
