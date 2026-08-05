namespace ActualChat;

public static partial class Constants
{
    public static class Audio
    {
        // Frame rate / cadence — every audio frame is independently decodable
        // (Opus has no keyframe dependency). Frame rate is derived from the
        // 20 ms Opus frame size, so FrameDuration is the canonical TimeSpan name
        // and OpusFrameDuration / OpusFrameDurationMs are aliases kept for
        // backwards compatibility with widely-used Opus-flavored call sites.
        public const int FrameRate = 50;
        public const int FrameDurationMs = 1000 / FrameRate; // 20
        public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(FrameDurationMs);
        public const int OpusFrameDurationMs = FrameDurationMs;
        public static readonly TimeSpan OpusFrameDuration = FrameDuration;

        // Audio buffer (intentional playback buffer, before decode).
        // Doc-target values from docs/audio-pipeline.md "Constants" block.
        public const int StartBufferSize = 5;
        public static readonly TimeSpan StartBufferDuration =
            TimeSpan.FromSeconds((double)StartBufferSize / FrameRate); // 100 ms
        public const int BufferHysteresisSize = 3;
        public const int MinBufferSize = StartBufferSize - BufferHysteresisSize; // 2

        // Voice-start pre-roll - bounded ring of recent encoded-frame-equivalent
        // PCM kept while voice activity is inactive, prepended to the first
        // encoded frame on voice start so leading consonants survive.
        public const int VoicePreRollFrameLimit = 15; // 300 ms

        // RPC stream flow control — split into recording (client→server upload,
        // must not skip recorded speech) and delivery (server→client, must not
        // skip frames on receiver's behalf). Doc-target values.
        public const int DeliveryRpcStreamAckPeriod = 10; // 200 ms @ 50 fps
        public const int RecordingRpcStreamAckPeriod = 10; // 200 ms @ 50 fps

        // Target audio-vs-video lag offset. 0 = exact sync. Was -100 ms (audio
        // kept ahead) to compensate unmeasured audio output latency — but the
        // audio lag metric already includes it (AudioContext baseLatency+
        // outputLatency on web, AudioEnginePlaybackLatency on native), so a
        // negative baseline made audio genuinely lead video. Keep ≥ 0; a small
        // positive would bias toward audio-slightly-behind (the safe side).
        public static readonly TimeSpan AudioCatchUpBaselineDelta = TimeSpan.Zero;
        // Max EXTRA audio delay the A/V-sync hold may add over the base playback
        // buffer. Audio is delayed to meet video's latency, but never by more than
        // this — so a far-behind video (e.g. ≥1.5 s) delays audio ≤1 s rather than
        // stalling it. Bounds both the start-time and runtime holds.
        public static readonly TimeSpan AudioSyncMaxHold = TimeSpan.FromSeconds(1);
        // Per-stream presentation-lag updates older than this are ignored when
        // aggregating per-author lag. Covers paused video, hidden tabs, and
        // stopped streams — all map to "no signal → desired = 0".
        public static readonly TimeSpan PlaybackLagStaleAfter = TimeSpan.FromMilliseconds(1500);

        // Other audio frame characteristics
        public const int VadFrameDurationMs = 32;
        public const int ApmFrameDurationMs = 10;
        public const int OpusFrameLength = RecordingSampleRate / 1000 * OpusFrameDurationMs;
        public const int VadFrameLength = RecordingSampleRate / 1000 * VadFrameDurationMs;
        public const int PcmFrameLength = PlaybackSampleRate / 1000 * OpusFrameDurationMs;
        public const int Bitrate = 32000;
        public const int Channels = 1;
        public const int RecordingSampleRate = 16000;
        public const int PlaybackSampleRate = 48000;

        // Streaming / channel sizing
        public const int StreamingChannelCapacity = 1024;
        public static readonly TimeSpan ListeningDuration = TimeSpan.FromSeconds(60);
        // Walkie-talkie mode (docs/superpowers/specs/2026-07-13-walkie-talkie-android-design.md).
        // Invariant: must stay > the server's NotificationsSettings.WalkieTalkieWakeTtl (30s).
        public static readonly TimeSpan WalkieTalkieIdleTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan WalkieTalkieIdleCheckPeriod = TimeSpan.FromSeconds(15);
        // Debounces GestureUI's activation loop: its inputs invalidate far more often than the
        // idle-check period, and it runs on battery-sensitive mobile clients.
        public static readonly TimeSpan WalkieTalkieGestureCheckMinPeriod = TimeSpan.FromSeconds(0.25);
        // Matches the wake push's FCM TTL; older wakes skip replay-from-start and go live.
        public static readonly TimeSpan WalkieTalkieStaleWakeAge = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan WalkieTalkieReplyColdStartTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan WalkieTalkieReplyBackgroundHotWindow = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan WalkieTalkieReplyRecencyWindow = TimeSpan.FromSeconds(150);
        // Apple PTT transmit: the framework chimes when it activates the session, not when our
        // recorder exists, so audio is captured natively across the gap. Capacity must stay <=
        // 10 s, which is AppleAudioCapture's outBuffer size at RecordingSampleRate.
        public static readonly TimeSpan WalkieTalkiePttTransmitStartupTimeout = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan WalkieTalkiePreRollCapacity = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan WalkieTalkiePreRollMinDuration = TimeSpan.FromSeconds(0.4);
        public static readonly TimeSpan WalkieTalkiePreRollFlushDelay = TimeSpan.FromSeconds(1.5);
        public static readonly TimeSpan RecordingDuration = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxRealtimeStreamDrift = TimeSpan.FromSeconds(1.5);
        public static readonly TimeSpan MaxStreamDuration = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan MaxBeginsAtDrift = TimeSpan.FromSeconds(5);

        // Watchdog: cancel ProcessAudio handler if no frame arrives within this window.
        // Opus frames are 20 ms; 2 s of silence means the producer is pathologically stalled.
        public static readonly TimeSpan FrameSilenceTimeout = TimeSpan.FromSeconds(2);

        // Legacy single-direction RPC stream flow control. Used today for the
        // server→client delivery path (AudioStreamingBackend.GetAudio,
        // LiveAudioStreams.GetStream/GetReplayStream, GetTranscript) and as the
        // client-side RPC defaults when the recording stream doesn't override
        // them. To be replaced by DeliveryRpcStreamAckPeriod /
        // RecordingRpcStreamAckPeriod in a follow-up step that splits the two
        // directions and adopts the doc-target 5-frame ACK cadence.
        public const int StreamAckPeriod = 64;
        public const int StreamBufferSize = 192;

        // Used by MAUI playback engines to decide when "buffer is low" and as a
        // start-playback minimum for the Windows engine. Not part of the doc's
        // target Constants block; will be reconsidered when the audio-buffer
        // refactor unifies start-threshold semantics across web and MAUI.
        public static readonly TimeSpan LowPlaybackBufferDuration = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan StartPlaybackWhenBufferedDuration = StartBufferDuration;

        // Pre-decoder (encoded frame) buffer sizing. The engine's pre-decoder
        // buffer target is computed as
        //   max(MinEncodeBufferSize, TrackInfo.TargetBufferSize - DefaultDecodedBufferSize - DefaultAudioEnginePlaybackLatency)
        // by the code that owns the pre-decoder buffer (TS decoder + MAUI engines).
        public static readonly TimeSpan MinEncodedBufferSize = TimeSpan.FromMilliseconds(30); // 2 frames
        public static readonly TimeSpan DecodedBufferSize = TimeSpan.FromMilliseconds(120); // 6 frames, must be exact
        public static readonly TimeSpan AudioEnginePlaybackLatency = TimeSpan.FromMilliseconds(40);
        public static readonly TimeSpan PlaybackTargetBufferSize = TimeSpan.FromMilliseconds(240);
        public static readonly TimeSpan PlaybackTargetBufferSizeWithVideo = TimeSpan.FromMilliseconds(280);
    }
}
