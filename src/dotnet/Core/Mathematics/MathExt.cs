namespace ActualChat.Mathematics;

public static class MathExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(this double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
