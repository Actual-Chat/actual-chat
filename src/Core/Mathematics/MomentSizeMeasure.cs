using System;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public sealed class MomentSizeMeasure : ISizeMeasure<Moment, TimeSpan>
    {
        public static MomentSizeMeasure Instance { get; } = new();

        public TimeSpan GetDistance(Moment start, Moment end) => end - start;
        public Moment AddOffset(Moment point, TimeSpan offset) => point + offset;

        public TimeSpan Add(TimeSpan first, TimeSpan second) => first + second;
        public TimeSpan Subtract(TimeSpan first, TimeSpan second) => first - second;
        public TimeSpan Multiply(TimeSpan size, double multiplier) => size * multiplier;
        public TimeSpan Modulo(TimeSpan size, TimeSpan modulo)
            => TimeSpan.FromTicks(size.Ticks % modulo.Ticks);
    }
}
