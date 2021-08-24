namespace ActualChat.Mathematics
{
    public sealed class LongSizeMeasure : ISizeMeasure<long, long>
    {
        public static LongSizeMeasure Instance { get; } = new();

        public long GetDistance(long start, long end) => end - start;
        public long AddOffset(long point, long offset) => point + offset;

        public long Add(long first, long second) => first + second;
        public long Subtract(long first, long second) => first - second;
        public long Multiply(long size, double multiplier) => (long) (size * multiplier);
        public long Modulo(long size, long modulo) => size % modulo;
    }
}
