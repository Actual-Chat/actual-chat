// Mirrors VoiceOnsetFinder.cs; tuned by VoicePreRollTrimFittingTest.

const MARGIN_FACTOR = 1.9952623149688795; // +6 dB
const MIN_BACKGROUND_FRAME_COUNT = 5;

const BANDS: readonly (readonly [number, number])[] = [
    [300, 600], [600, 1200], [1200, 2400], [2400, 4000], [4000, 7000],
];

/**
 * Returns how many of the newest frames hold speech, at most `lookbackFrameCount`: walks back from the
 * newest frame while any of five frequency bands stays 6 dB above its own background.
 */
export function getSpeechFrameCount(
    frames: readonly Float32Array[],
    sampleRate: number,
    lookbackFrameCount: number,
): number {
    const frameCount = frames.length;
    if (frameCount === 0)
        return 0;

    const lookbackStart = Math.max(0, frameCount - lookbackFrameCount);
    const bands = BANDS.map(band => impl(band));
    for (let i = frameCount - 1; i >= lookbackStart; i--) {
        const isQuiet = bands.every(({ levels, threshold }) => levels[i] <= threshold);
        if (isQuiet)
            return frameCount - 1 - i;
    }

    return frameCount - lookbackStart;

    /**
     * Band-passes all frames in order (RBJ, 0 dB peak) and returns per-frame RMS plus background + 6 dB;
     * the background comes from frames older than the lookback window, or the quietest ones if too few.
     */
    function impl([low, high]: readonly [number, number]): { levels: number[], threshold: number } {
        const center = Math.sqrt(low * high);
        const w0 = 2 * Math.PI * center / sampleRate;
        const alpha = Math.sin(w0) * (high - low) / (2 * center);
        const a0 = 1 + alpha;
        const b0 = alpha / a0;
        const a1 = -2 * Math.cos(w0) / a0;
        const a2 = (1 - alpha) / a0;
        let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        const levels = frames.map(frame => {
            let sum = 0;
            for (const x of frame) {
                const y = b0 * x - b0 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
                sum += y * y;
            }
            return Math.sqrt(sum / frame.length);
        });

        const hasOlderFrames = lookbackStart >= MIN_BACKGROUND_FRAME_COUNT;
        const sorted = (hasOlderFrames ? levels.slice(0, lookbackStart) : levels.slice()).sort((a, b) => a - b);
        const percentile = hasOlderFrames ? 0.3 : 0.1;
        const background = sorted[Math.round(percentile * (sorted.length - 1))];
        return { levels, threshold: background * MARGIN_FACTOR };
    }
}
