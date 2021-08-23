using System;
using System.Collections.Generic;
using Stl;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public sealed class MomentLogCover : ConvertingLogCover<Moment, TimeSpan>
    {
        public static MomentLogCover Default { get; } =
            new(new DoubleLogCover() {
                Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
                MinSpanSize = TimeSpan.FromMinutes(3).TotalSeconds,
                MaxSpanSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
                SpanSizeMultiplier = 4,
            });

        public MomentLogCover(DoubleLogCover baseCover)
            : base(
                baseCover,
                m => m.ToUnixEpoch(),
                ts => ts.TotalSeconds,
                s => new Moment(TimeSpan.FromSeconds(s)),
                TimeSpan.FromSeconds)
        { }

        public bool IsValidSpan(MomentRange span)
            => IsValidSpan(span.Start, span.End);

        public Option<MomentRange> TryGetSpan(MomentRange innerSpan)
        {
            var result = TryGetSpan(innerSpan.Start, innerSpan.End);
            if (result.IsSome(out var some))
                return Option.Some<MomentRange>((some.Start, some.End));
            return Option.None<MomentRange>();
        }

        public MomentRange GetSpan(MomentRange innerSpan)
            => GetSpan(innerSpan.Start, innerSpan.End);

        public new IEnumerable<MomentRange> GetSpans(Moment innerPoint)
        {
            foreach (var size in SpanSizes) {
                var secondsSize = size.TotalSeconds;
                var offset = (innerPoint - Zero).TotalSeconds % secondsSize;
                var start = innerPoint - TimeSpan.FromSeconds(offset);
                yield return (start, start + size);
            }
        }
    }
}
