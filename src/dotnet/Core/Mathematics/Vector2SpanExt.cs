using System.Numerics;

namespace ActualChat.Mathematics;

public static class Vector2SpanExt
{
    public static int IndexOfLowerOrEqualX(this ReadOnlySpan<Vector2> values, float x)
    {
        var result = -1;
        var minIndex = 0;
        var maxIndex = values.Length - 1;
        while (minIndex <= maxIndex) {
            var index = minIndex + ((maxIndex - minIndex) >> 1);
            var diff = values[index].X - x;
            if (diff <= 0) {
                result = index;
                minIndex = index + 1;
            }
            else
                maxIndex = index - 1;
        }
        return result;
    }

    public static int IndexOfGreaterOrEqualX(this ReadOnlySpan<Vector2> values, float x)
    {
        var result = -1;
        var minIndex = 0;
        var maxIndex = values.Length - 1;
        while (minIndex <= maxIndex) {
            var index = minIndex + ((maxIndex - minIndex) >> 1);
            var diff = values[index].X - x;
            if (diff < 0)
                minIndex = index + 1;
            else {
                result = index;
                maxIndex = index - 1;
            }
        }
        return result;
    }

    public static bool IsStrictlyIncreasingXSequence(this ReadOnlySpan<Vector2> values)
    {
        if (values.Length < 2)
            return true;

        var lastValue = values[0];
        for (var i = 1; i < values.Length; i++) {
            var value = values[i];
            if (value.X <= lastValue.X)
                return false;
            lastValue = value;
        }
        return true;
    }

    public static bool IsStrictlyIncreasingYSequence(this ReadOnlySpan<Vector2> values)
    {
        if (values.Length < 2)
            return true;

        var lastValue = values[0];
        for (var i = 1; i < values.Length; i++) {
            var value = values[i];
            if (value.Y <= lastValue.Y)
                return false;
            lastValue = value;
        }
        return true;
    }
}
