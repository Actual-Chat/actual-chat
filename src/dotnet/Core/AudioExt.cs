using System.Numerics;

namespace ActualChat;

public static class AudioExt
{
    public static double ApproximateGain(ReadOnlySpan<float> monoPcm, int stride = 5)
    {
        if (monoPcm.Length == 0)
            return 0;

        // Fast SIMD path for contiguous data
        if (stride == 1 && Vector.IsHardwareAccelerated) {
            var vecSize = Vector<float>.Count;
            var len = monoPcm.Length;
            var simdEnd = len - (len % vecSize);

            var vectorSum = Vector<float>.Zero;
            var i = 0;
            while (i < simdEnd) {
                var v = new Vector<float>(monoPcm.Slice(i, vecSize));
                vectorSum += v * v;
                i += vecSize;
            }

            var tailSum = 0f;
            for (; i < len; i++) {
                var e = monoPcm[i];
                tailSum += e * e;
            }

            var acc = 0f;
            for (var j = 0; j < vecSize; j++)
                acc += vectorSum[j];

            double total = acc + tailSum;
            return Math.Sqrt(total / len);
        }

        // Generic stride path (default: 5), unrolled to reduce overhead
        double sum = 0;
        int count = 0;
        int i2 = 0;

        // Unroll by 4 stride-steps per iteration
        for (; i2 + 4 * stride <= monoPcm.Length; i2 += 4 * stride) {
            float e0 = monoPcm[i2];
            float e1 = monoPcm[i2 + stride];
            float e2 = monoPcm[i2 + 2 * stride];
            float e3 = monoPcm[i2 + 3 * stride];
            sum += (double)e0 * e0
                + (double)e1 * e1
                + (double)e2 * e2
                + (double)e3 * e3;
            count += 4;
        }

        for (; i2 < monoPcm.Length; i2 += stride) {
            float e = monoPcm[i2];
            sum += e * e;
            count++;
        }

        if (count <= 0) return 0;
        return Math.Sqrt(sum / count);
    }
}
