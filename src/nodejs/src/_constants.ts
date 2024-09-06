// All crucial constants are here

const REC_SAMPLES_PER_MS = 16; // 16KHz
export const AUDIO_REC = {
    SAMPLE_RATE: 1000 * REC_SAMPLES_PER_MS,
    SAMPLES_PER_MS: REC_SAMPLES_PER_MS,
    SAMPLES_PER_WINDOW_32: REC_SAMPLES_PER_MS * 32,
    SAMPLES_PER_WINDOW_30: REC_SAMPLES_PER_MS * 30,
    SAMPLES_PER_RECORDING_IN_PROGRESS_CALL: REC_SAMPLES_PER_MS * 200,
    // In seconds:
    SAMPLE_DURATION: 0.001 / REC_SAMPLES_PER_MS,
}

const PLAY_SAMPLES_PER_MS = 48; // 48KHz
export const AUDIO_PLAY = {
    SAMPLE_RATE: 1000 * PLAY_SAMPLES_PER_MS,
    SAMPLES_PER_MS: PLAY_SAMPLES_PER_MS,
    SAMPLES_PER_WINDOW: 20 * PLAY_SAMPLES_PER_MS, // 20ms
    // In seconds:
    SAMPLE_DURATION: 0.001 / PLAY_SAMPLES_PER_MS,
    BUFFER_TO_PLAY_DURATION: 0.15,
    BUFFER_TO_STARVE_DURATION: 10.0,
    STATE_UPDATE_PERIOD: 0.2,
}

export const AUDIO_ENCODER = {
    BIT_RATE: 32 * 1024, // 32Kbps
    FRAME_SIZE: REC_SAMPLES_PER_MS * 20, // 20ms
    BUFFER_FRAMES: 6, // = 120ms
    FADE_FRAMES: 3, // = 6ms
    FRAMES_TO_SEND_ON_RESUME: 5, // = 100ms
    FRAMES_TO_SEND_ON_RECONNECT: 1000, // = 20s
    DEFAULT_PRE_SKIP_FRAMES: 312, // Encoder provides this value, buy
}

export const AUDIO_VAD = {
    // All durations here are in seconds
    MIN_SILENCE: 0.20, // Min silence duration that triggers pause
    MIN_SPEECH: 0.5, // ?
    MAX_SILENCE: 1.35, // 1.35 s - max silence duration that triggers pause
    MAX_SPEECH: 60 * 2, // max speech duration, it will be split by zero pause afterward
    MAX_MONOLOGUE_SILENCE: 3, // max silence when you're the only one talking
    MAX_SILENCE_VARIES_FROM: 45, // Once you talk for 45 seconds, max silence starts to decrease
    NON_MONOLOGUE_DURATION: 30, // A period from conversationSignal to the moment VAD assumes it's a monologue again
};
