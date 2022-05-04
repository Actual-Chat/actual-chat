namespace ActualChat;

public static class NumberExt
{
    public static int Clamp(this int value, int min, int max)
        => Math.Min(max, Math.Max(min, value));
    public static long Clamp(this long value, long min, long max)
        => Math.Min(max, Math.Max(min, value));
    public static float Clamp(this float value, float min, float max)
        => Math.Min(max, Math.Max(min, value));
    public static double Clamp(this double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));
}
