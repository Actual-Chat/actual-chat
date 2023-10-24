namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static string Format<T>(this Range<T> range)
        where T : notnull
        => $"[{range.Start}, {range.End})";
}
