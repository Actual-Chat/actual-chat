using System.Numerics;

namespace ActualChat;

public static class AudioExt
{
    private const float SoftLimitKnee = 0.7f;

    public static void Amplify(Span<float> pcm, float gain)
    {
        // Above the knee the curve bends toward 1.0 instead of clipping, so a boosted peak
        // compresses rather than crackles.
        const float range = 1f - SoftLimitKnee;
        for (var i = 0; i < pcm.Length; i++) {
            var sample = pcm[i] * gain;
            var magnitude = MathF.Abs(sample);
            if (magnitude <= SoftLimitKnee) {
                pcm[i] = sample;
                continue;
            }

            var limited = SoftLimitKnee + range * MathF.Tanh((magnitude - SoftLimitKnee) / range);
            pcm[i] = MathF.CopySign(limited, sample);
        }
    }

    public static double ApproximateGain(ReadOnlySpan<float> monoPcm, int stride = 5)
    {
        if (monoPcm.Length == 0) return 0;

        // Estimate output count
        int sampleCount = (monoPcm.Length + stride - 1) / stride;
        if (sampleCount == 0) return 0;

        // Fast path: stride == 1 && SIMD available → use original span directly
        if (stride == 1 && Vector.IsHardwareAccelerated)
            return ComputeRmsSimd(monoPcm);

        // General path: decimate into contiguous buffer, then use SIMD if available
        Span<float> samples = sampleCount <= 1024
            ? stackalloc float[sampleCount]
            : new float[sampleCount];

        int di = 0;
        for (int si = 0; si < monoPcm.Length; si += stride)
            samples[di++] = monoPcm[si];

        return Vector.IsHardwareAccelerated
            ? ComputeRmsSimd(samples)
            : ComputeRmsScalar(samples);
    }

    // SIMD-accelerated RMS on contiguous span
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeRmsSimd(ReadOnlySpan<float> samples)
    {
        var vectorSum = Vector<float>.Zero;
        int vecSize = Vector<float>.Count;
        int i = 0;
        int simdEnd = samples.Length - (samples.Length % vecSize);

        while (i < simdEnd)
        {
            var v = new Vector<float>(samples.Slice(i, vecSize));
            vectorSum += v * v;
            i += vecSize;
        }

        float tailSum = 0f;
        for (; i < samples.Length; i++)
        {
            float e = samples[i];
            tailSum += e * e;
        }

        float acc = 0f;
        for (int j = 0; j < vecSize; j++)
            acc += vectorSum[j];

        double total = acc + tailSum;
        return Math.Sqrt(total / samples.Length);
    }

    // Fallback scalar RMS
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeRmsScalar(ReadOnlySpan<float> samples)
    {
        double sum = 0.0;
        foreach (var e in samples)
            sum += e * e;
        return samples.Length > 0 ? Math.Sqrt(sum / samples.Length) : 0.0;
    }
}
