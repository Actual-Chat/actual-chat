// All crucial constants are here
// !DELAYER means it affects on audio delay.

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
    BUFFER_TO_PLAY_DURATION: 0.1, // !DELAYER: How much to buffer before we start playing
    BUFFER_LOW_DURATION: 10.0, // Buffer is "low" while it's less than this
    STATE_UPDATE_PERIOD: 0.2, // The period between feeder state update signals
}

export const AUDIO_ENCODER = {
    BIT_RATE: 32 * 1024, // 32Kbps
    FRAME_SIZE: REC_SAMPLES_PER_MS * 20, // 20ms
    BUFFER_FRAMES: 5, // 100ms - !DELAYER: encoder won't proceed unless that much is buffered
    FADE_FRAMES: 3, // 60ms
    MAX_FRAMES: 1500, // 30s
    DEFAULT_PRE_SKIP_FRAMES: 312, // Encoder provides this value, buy
}

export const AUDIO_VAD = {
    // All durations here are in seconds
    MIN_SPEECH: 0.5, // When the speech is detected, it will always send at this much at least
    MAX_SPEECH: 60 * 2, // Max speech duration - if it takes longer, VAD will generate a hard split anyway
    MIN_SPEECH_TO_CANCEL_PAUSE: 0.15, // Min. speech duration required to cancel non-materialized pause
    MIN_PAUSE: 0.2, // Min pause duration that triggers split
    MAX_PAUSE: 2.7, // Max pause duration that triggers split
    MAX_CONV_PAUSE: 1.35, // Max pause duration that triggers pause in "conversation" mode
    CONV_DURATION: 30, // A period from conversationSignal to the moment VAD assumes the conversation ended
    PAUSE_VARIES_FROM: 10, // Pause starts to vary from (MAX_PAUSE or MAX_CONV_PAUSE) to MIN_PAUSE at this speech duration
    PAUSE_VARY_POWER: Math.sqrt(2), // The power used in max_pause = lerp(MAX_PAUSE, MIN_PAUSE, pow(alpha, THIS_VALUE))
};
