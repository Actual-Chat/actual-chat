namespace ActualChat.Mathematics.Internal;

internal sealed class LongSizeMeasure : SizeMeasure<long, long>
{
    public override long GetDistance(long start, long end) => end - start;
    public override long AddOffset(long point, long offset) => point + offset;

    public override long Add(long first, long second) => first + second;
    public override long Subtract(long first, long second) => first - second;
    public override long Multiply(long size, double multiplier) => (long) (size * multiplier);

    public override long Modulo(long size, long modulo)
    {
        var result = size % modulo;
        return result >= 0 ? result : modulo + result;
    }
}
