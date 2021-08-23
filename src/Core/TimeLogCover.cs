using System;
using System.Linq;
using Stl;
using Stl.Time;

namespace ActualChat
{
    public sealed class TimeLogCover : LogCover<Moment>
    {
        public static TimeLogCover Default { get; } =
            new(new LogCover() {
                Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
                MinSpanSize = TimeSpan.FromMinutes(3).TotalSeconds,
                MaxSpanSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
                SpanSizeMultiplier = 4,
            });

        public TimeSpan MinSpanSize { get; }
        public TimeSpan MaxSpanSize { get; }
        public TimeSpan[] SpanSizes { get; }

        public TimeLogCover(LogCover baseConverage)
            : base(
                baseConverage,
                m => m.ToUnixEpoch(),
                s => new Moment(TimeSpan.FromSeconds(s)))
        {
            MinSpanSize = MinSpanEnd - ZeroPoint;
            MaxSpanSize = MaxSpanEnd - ZeroPoint;
            SpanSizes = SpanEnds.Select(e => e - ZeroPoint).ToArray();
        }

        public Option<TimeRange> TryGetSpan(TimeRange innerRange)
        {
            var result = TryGetSpan(innerRange.Start, innerRange.End);
            if (result.IsSome(out var some))
                return Option.Some<TimeRange>((some.Start, some.End));
            return Option.None<TimeRange>();
        }

        public TimeRange GetSpan(TimeRange innerRange)
            => GetSpan(innerRange.Start, innerRange.End);

        public bool IsValidSpan(TimeRange unit)
            => IsValidSpan(unit.Start, unit.End);
    }
}
