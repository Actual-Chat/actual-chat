namespace ActualChat.Audio;

/// <summary>
/// Finds where speech starts in the audio buffered before a voice-activity start: walks back from the newest
/// frame while any of five frequency bands stays 6 dB above its own background.
/// Mirrored by <c>voice-onset.ts</c>; tuned by <c>VoicePreRollTrimFittingTest</c>.
/// </summary>
public static class VoiceOnsetFinder
{
    private const double MarginFactor = 1.9952623149688795; // +6 dB
    private const int MinBackgroundFrameCount = 5;

    private static readonly (double Low, double High)[] Bands =
        [(300, 600), (600, 1200), (1200, 2400), (2400, 4000), (4000, 7000)];

    public static int GetSpeechFrameCount(
        ReadOnlySpan<float> samples,
        int frameLength,
        int sampleRate,
        int lookbackFrameCount)
    {
        // Returns how many of the newest frames hold speech, at most lookbackFrameCount
        var frameCount = samples.Length / frameLength;
        if (frameCount == 0)
            return 0;

        var lookbackStart = Math.Max(0, frameCount - lookbackFrameCount);
        var levels = new double[Bands.Length][];
        var thresholds = new double[Bands.Length];
        for (var b = 0; b < Bands.Length; b++) {
            levels[b] = GetBandLevels(samples, frameLength, frameCount, sampleRate, Bands[b]);
            thresholds[b] = GetBackground(levels[b], lookbackStart) * MarginFactor;
        }
        for (var i = frameCount - 1; i >= lookbackStart; i--) {
            var isQuiet = true;
            for (var b = 0; b < Bands.Length && isQuiet; b++)
                isQuiet = levels[b][i] <= thresholds[b];
            if (isQuiet)
                return frameCount - 1 - i;
        }

        return frameCount - lookbackStart;
    }

    // Private methods

    private static double[] GetBandLevels(
        ReadOnlySpan<float> samples,
        int frameLength,
        int frameCount,
        int sampleRate,
        (double Low, double High) band)
    {
        // RBJ band-pass with 0 dB peak gain, run over the whole buffer; returns per-frame RMS
        var center = Math.Sqrt(band.Low * band.High);
        var w0 = 2 * Math.PI * center / sampleRate;
        var alpha = Math.Sin(w0) * (band.High - band.Low) / (2 * center);
        var a0 = 1 + alpha;
        var b0 = alpha / a0;
        var a1 = -2 * Math.Cos(w0) / a0;
        var a2 = (1 - alpha) / a0;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        var levels = new double[frameCount];
        for (var f = 0; f < frameCount; f++) {
            var sum = 0.0;
            var frame = samples.Slice(f * frameLength, frameLength);
            foreach (var x in frame) {
                var y = b0 * x - b0 * x2 - a1 * y1 - a2 * y2;
                (x2, x1, y2, y1) = (x1, x, y1, y);
                sum += y * y;
            }
            levels[f] = Math.Sqrt(sum / frameLength);
        }
        return levels;
    }

    private static double GetBackground(double[] levels, int olderFrameCount)
    {
        // Frames older than the lookback window hold the pause before speech; a short buffer
        // has no such frames, so its quietest ones stand in for them
        var hasOlderFrames = olderFrameCount >= MinBackgroundFrameCount;
        var sorted = (hasOlderFrames ? levels.AsSpan(0, olderFrameCount) : levels).ToArray();
        Array.Sort(sorted);
        var percentile = hasOlderFrames ? 0.3 : 0.1;
        return sorted[(int)Math.Round(percentile * (sorted.Length - 1))];
    }
}
