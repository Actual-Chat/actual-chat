namespace ActualChat;

// ReSharper disable UnusedMember.Global

partial record AppConstants
{
    /// <summary>
    /// JS-interop snapshot of audio-pipeline constants.
    /// Serialized to TS via BrowserInit and exposed there as the <c>AUDIO</c> field.
    /// Derived values (sample counts, byte counts, second-unit aliases, etc.)
    /// are computed TS-side in <c>initAppConstants</c> rather than shipped over the wire.
    /// </summary>
    public sealed record AudioConstants
    {
        // Audio-wide cadence — every audio frame is 20 ms / 50 fps.
        public int FrameRate { get; init; } = Constants.Audio.FrameRate;

        public RecConstants Rec { get; init; } = new();
        public PlayConstants Play { get; init; } = new();
        public EncodeConstants Encode { get; init; } = new();
        public StreamConstants Stream { get; init; } = new();
        public VadConstants Vad { get; init; } = new();

        public sealed record RecConstants
        {
            public int SampleRate { get; init; } = Constants.Audio.RecordingSampleRate;
            public double MinRecordingGain { get; init; } = 0.0005;
            public double MinMicrophoneGain { get; init; } = 0;
            // Period at which the encoder worklet emits "recording in progress" signals
            // back to the main thread (TS-only).
            public int RecordingInProgressReportPeriodMs { get; init; } = 200;
            public HeartbeatConstants Heartbeat { get; init; } = new();

            public sealed record HeartbeatConstants
            {
                public int IntervalMs { get; init; } = 3000;
                public int TimeoutMs { get; init; } = 12000;
                public int CheckIntervalMs { get; init; } = 500;
            }
        }

        public sealed record PlayConstants
        {
            public int SampleRate { get; init; } = Constants.Audio.PlaybackSampleRate;
            // Doc-target playback buffer sizing (audio-pipeline.md "Constants" block).
            // StartBufferSize is the input; StartBufferDurationMs (= size / frameRate)
            // and MinBufferSize (= size - hysteresis) are derived TS-side.
            public int StartBufferSize { get; init; } = Constants.Audio.StartBufferSize;
            public int BufferHysteresisSize { get; init; } = Constants.Audio.BufferHysteresisSize;
            // How much to grow the start-buffer threshold during starvation.
            public int StartBufferGrowDurationMs { get; init; } = 100;
            // !DELAYER: Larger start-buffer when video is active for A/V sync.
            public int StartBufferDurationWithVideoMs { get; init; } = 500;
            // Pre-decoder buffer formula inputs — used TS-side by the OpusDecoder
            // to size its encoded-frame buffer from TrackInfo.TargetBufferSize:
            //   encoded = max(MinEncodeBuffer, target - DefaultDecodedBuffer - DefaultAudioEnginePlaybackLatency)
            public int MinEncodedBufferSizeMs { get; init; } =
                (int)Constants.Audio.MinEncodedBufferSize.TotalMilliseconds;
            public int DecodedBufferSizeMs { get; init; } =
                (int)Constants.Audio.DecodedBufferSize.TotalMilliseconds;
            public int AudioEnginePlaybackLatencyMs { get; init; } =
                (int)Constants.Audio.AudioEnginePlaybackLatency.TotalMilliseconds;
            // Buffer is "low" while it's less than this.
            public int LowBufferDurationMs { get; init; } = 10000;
            // Period between feeder state-update signals.
            public int StateUpdatePeriodMs { get; init; } = 200;
            // Debounce window for resetting iOS media-session metadata.
            public int MediaSessionResetDebounceMs { get; init; } = 5000;
            // Audio buffer playback catch-up policy (used when paired with video
            // from the same author; a separate step will wire these in).
            public int PlaybackHardSkipThresholdMs { get; init; } =
                (int)Constants.Audio.PlaybackHardSkipThreshold.TotalMilliseconds;
            public int PlaybackMaxSpeedUpDurationMs { get; init; } =
                (int)Constants.Audio.PlaybackMaxSpeedUpDuration.TotalMilliseconds;
            public int PlaybackSpeedUpDropEveryNFrames { get; init; } =
                Constants.Audio.PlaybackSpeedUpDropEveryNFrames;
        }

        public sealed record EncodeConstants
        {
            // FrameDurationMs is derived from audio.frameRate TS-side.
            public int Bitrate { get; init; } = Constants.Audio.Bitrate;
            public int Channels { get; init; } = Constants.Audio.Channels;
            // Fade-in @ start and fade-out @ end of a recording, in encoder frames.
            public int FadeFrames { get; init; } = 3;
            // Encoder-internal output buffer cap, in frames.
            public int MaxBufferedFrames { get; init; } = 50;
            // Pre-skip / codec delay measured in samples — default for system encoder
            // when it doesn't report its own.
            public int DefaultPreSkip { get; init; } = 312;
            // Voice-start pre-roll - bounded ring of recent encoder-frame-equivalent
            // PCM kept while voice activity is inactive; prepended to the first
            // encoded frame on voice start so leading consonants survive.
            public int VoicePreRollFrameLimit { get; init; } = Constants.Audio.VoicePreRollFrameLimit;
        }

        public sealed record StreamConstants
        {
            public int MaxStreams { get; init; } = 3;
            public int MinPackFrames { get; init; } = 3;
            public int MaxPackFrames { get; init; } = 10;
            public int MaxBufferedFrames { get; init; } = 1500;
            // Max streaming speed relative to real-time.
            public int MaxSpeed { get; init; } = 2;
            public int InterStreamDelayMs { get; init; } = 100;
            public int StreamErrorDelayMs { get; init; } = 1000;
            public int ConnectErrorDelayMs { get; init; } = 1000;
            // Debug switch — random disconnect period (0 = disabled).
            public int DebugRandomDisconnectPeriodMs { get; init; } = 0;
            public int RecordingRpcStreamAckPeriod { get; init; } = Constants.Audio.RecordingRpcStreamAckPeriod;
            public int DeliveryRpcStreamAckPeriod { get; init; } = Constants.Audio.DeliveryRpcStreamAckPeriod;
            // Legacy aliases kept for older TS call sites.
            public int RpcAckPeriod { get; init; } = Constants.Audio.StreamAckPeriod;
            public int RpcBufferSize { get; init; } = Constants.Audio.StreamBufferSize;
        }

        public sealed record VadConstants
        {
            // NN VAD frame duration (Silero etc.). Was Constants.Audio.VadFrameDurationMs.
            public int NeuralFrameDurationMs { get; init; } = Constants.Audio.VadFrameDurationMs;
            // Legacy WebRTC VAD frame duration.
            public int WebrtcFrameDurationMs { get; init; } = 30;
            public int ApmFrameDurationMs { get; init; } = Constants.Audio.ApmFrameDurationMs;
            // When speech is detected, send at least this much.
            public int MinSpeechMs { get; init; } = 500;
            // Hard split if speech goes longer than this.
            public int MaxSpeechMs { get; init; } = 120000;
            // Min. speech to cancel a non-materialized pause.
            public int MinSpeechToCancelPauseMs { get; init; } = 150;
            public int MinPauseMs { get; init; } = 200;
            public int MaxPauseMs { get; init; } = 2700;
            public int MaxConvPauseMs { get; init; } = 650;
            // Period from conversationSignal until VAD assumes the conversation ended.
            public int ConvDurationMs { get; init; } = 30000;
            // Speech duration at which pause starts to vary toward MinPause.
            public int PauseVariesFromMs { get; init; } = 10000;
            // Power used in: max_pause = lerp(MAX_PAUSE, MIN_PAUSE, alpha^THIS).
            public double PauseVaryPower { get; init; } = 1.4142135623730951; // sqrt(2)
            // Microphone stream begins with noise that triggers WebRTC VAD — skip this initial period.
            public int SkipFirstRecordingMs { get; init; } = 300;
            // Skip batch calls to VAD when calls are this close together.
            public int SkipSequentialCallsMs { get; init; } = 5;
            // NN VAD buffer "context" prefix copied from end of prev. buffer (samples).
            public int NnVadContextSamples { get; init; } = 64;
        }
    }
}
