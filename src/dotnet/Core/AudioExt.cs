namespace ActualChat;

public static class AudioExt
{
    public static double ApproximateGain(ReadOnlySpan<float> monoPcm, int stride = 5)
    {
        double sum = 0;
        // every 5th sample as usually it's enough to assess speech gain
        for (int i = 0; i < monoPcm.Length; i += stride) {
            float e = monoPcm[i];
            sum += e * e;
        }
        return Math.Sqrt(sum / Math.Floor((double)monoPcm.Length / stride));
    }
}
