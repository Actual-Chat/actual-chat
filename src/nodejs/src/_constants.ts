// All crucial constants are here
// !DELAYER means it affects on audio delay.

type AllowedSampleRate = 48000 | 16000;

const REC_SAMPLES_PER_MS = 16; // 16KHz
export const AUDIO_REC = {
    SAMPLE_RATE: 1000 * REC_SAMPLES_PER_MS as AllowedSampleRate,
    SAMPLES_PER_MS: REC_SAMPLES_PER_MS,
    SAMPLES_PER_WINDOW_32: REC_SAMPLES_PER_MS * 32,
    SAMPLES_PER_WINDOW_30: REC_SAMPLES_PER_MS * 30,
    SAMPLES_PER_RECORDING_IN_PROGRESS_CALL: REC_SAMPLES_PER_MS * 200,
    // In seconds:
    SAMPLE_DURATION: 0.001 / REC_SAMPLES_PER_MS,
}

const PLAY_SAMPLES_PER_MS = 48; // 48KHz
export const AUDIO_PLAY = {
    SAMPLE_RATE: 1000 * PLAY_SAMPLES_PER_MS as AllowedSampleRate,
    SAMPLES_PER_MS: PLAY_SAMPLES_PER_MS,
    SAMPLES_PER_WINDOW: 20 * PLAY_SAMPLES_PER_MS, // 20ms
    // In seconds:
    SAMPLE_DURATION: 0.001 / PLAY_SAMPLES_PER_MS,
    BUFFER_TO_PLAY_DURATION: 0.1, // !DELAYER: How much to buffer before we start playing
    BUFFER_LOW_DURATION: 10.0, // Buffer is "low" while it's less than this
    STATE_UPDATE_PERIOD: 0.2, // The period between feeder state update signals
    MEDIA_SESSION_RESET_DEBOUNCE_MS: 5000, // The period to debounce media session metadata (displays info that Actual Chat is playing at iOS) reset
}

const ENC_FRAME_DURATION_MS = 20; // 20ms
const ENC_BIT_RATE = 32 * 1024; // 32Kbps = 4KB/s = ~80 bytes per frame
const ENC_BYTE_RATE = Math.round(ENC_BIT_RATE / 8);
const ENC_FRAME_BYTES = Math.round(ENC_BIT_RATE * ENC_FRAME_DURATION_MS / 1000 / 8);
export const AUDIO_ENCODER = {
    FRAME_DURATION_MS: ENC_FRAME_DURATION_MS,
    BIT_RATE: ENC_BIT_RATE,
    BYTE_RATE: ENC_BYTE_RATE,
    FRAME_SAMPLES: REC_SAMPLES_PER_MS * ENC_FRAME_DURATION_MS,
    FRAME_BYTES: ENC_FRAME_BYTES,
    FRAME_BUFFER_BYTES: 2 * ENC_FRAME_BYTES,
    FADE_FRAMES: 3, // 60ms, it's fade-in @ start and fade-out @ the end
    MAX_BUFFERED_FRAMES: 50, // 1s, it doesn't have to be large now, coz frames are instantly added to streams
    DEFAULT_PRE_SKIP: 312, // Pre-skip / codec delay measured in samples. It's the default one, used w/ system encoder, which doesn't provide it.
}

export const AUDIO_STREAMER = {
    MAX_STREAMS: 3, // Max streams to keep sending
    DELAY_FRAMES: 5, // 100ms - !DELAYER: streamer won't start sending until it gets these frames (~400 bytes)
    MIN_PACK_FRAMES: 3, // 60ms - min. # of frames to send at once (~240 bytes)
    MAX_PACK_FRAMES: 10, // 200ms - max. # of frames to send at once (~800 bytes)
    MAX_BUFFERED_FRAMES: 1500, // 30s (~120KB)
    MAX_SPEED: 2, // Max streaming speed - relative to real-time; must be >1, but not too high to avoid queued stream overlap
    // In seconds:
    INTER_STREAM_DELAY: 0.1, // Must be >0
    MAX_CONNECT_DURATION: 5, // Max duration of "connecting" state of SignalR connection inside ensureConnected
    MAX_QUICK_CONNECT_DURATION: 0.5, // Same as above, but when quickReconnect = true
    STREAM_ERROR_DELAY: 1,
    CONNECT_ERROR_DELAY: 1,
    DEBUG: { // Debug switches
        RANDOM_DISCONNECT_PERIOD_MS: 0, // Random disconnect period, 5000 is a good one
    }
}

export const AUDIO_VAD = {
    // All durations below are in seconds
    MIN_SPEECH: 0.5, // When the speech is detected, it will always send at this much at least
    MAX_SPEECH: 60 * 2, // Max speech duration - if it takes longer, VAD will generate a hard split anyway
    MIN_SPEECH_TO_CANCEL_PAUSE: 0.15, // Min. speech duration required to cancel non-materialized pause
    MIN_PAUSE: 0.2, // Min pause duration that triggers split
    MAX_PAUSE: 2.7, // Max pause duration that triggers split
    MAX_CONV_PAUSE: 0.65, // Max pause duration that triggers pause in "conversation" mode
    CONV_DURATION: 30, // A period from conversationSignal to the moment VAD assumes the conversation ended
    PAUSE_VARIES_FROM: 10, // Pause starts to vary from (MAX_PAUSE or MAX_CONV_PAUSE) to MIN_PAUSE at this speech duration
    PAUSE_VARY_POWER: Math.sqrt(2), // The power used in max_pause = lerp(MAX_PAUSE, MIN_PAUSE, pow(alpha, THIS_VALUE))
    SKIP_FIRST_RECORDING_MS: 300, // Microphone stream begins with some noise that triggers WebRTC VAD
    SKIP_SEQUENTIAL_CALLS_MS: 5, // Skip batch calls to VAD when duration between calls is less than 5ms - VAD works with 30ms and 32 ms chunks
    // All durations below are in samples (16KHz = 16 samples per ms)
    NN_VAD_CONTEXT_SAMPLES: 64, // 4ms, the length of NN VAD buffer "prefix" copied from the end of prev. buffer
};
