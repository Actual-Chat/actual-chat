using System;
using Stl.Time;

namespace ActualChat.Mathematics.Internal
{
    internal sealed class MomentSizeMeasure : SizeMeasure<Moment, TimeSpan>
    {
        public override TimeSpan GetDistance(Moment start, Moment end) => end - start;
        public override Moment AddOffset(Moment point, TimeSpan offset) => point + offset;

        public override TimeSpan Add(TimeSpan first, TimeSpan second) => first + second;
        public override TimeSpan Subtract(TimeSpan first, TimeSpan second) => first - second;
        public override TimeSpan Multiply(TimeSpan size, double multiplier) => size * multiplier;
        public override TimeSpan Modulo(TimeSpan size, TimeSpan modulo)
            => TimeSpan.FromTicks(size.Ticks % modulo.Ticks);
    }
}
