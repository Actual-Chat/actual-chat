using System;
using System.Linq;
using Stl;
using Stl.Time;

namespace ActualChat
{
    public class TimeLogCoverage : LogCoverage<Moment>
    {
        public static TimeLogCoverage Default { get; } =
            new(new LogCoverage() {
                Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
                MinUnitSize = TimeSpan.FromMinutes(3).TotalSeconds,
                MaxUnitSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
                UnitSizeMultiplier = 4,
            });

        public TimeSpan MinUnitSize { get; }
        public TimeSpan MaxUnitSize { get; }
        public TimeSpan[] UnitSizes { get; }

        public TimeLogCoverage(LogCoverage baseConverage)
            : base(
                baseConverage,
                m => m.ToUnixEpoch(),
                s => new Moment(TimeSpan.FromSeconds(s)))
        {
            MinUnitSize = MinUnitEnd - ZeroPoint;
            MaxUnitSize = MaxUnitEnd - ZeroPoint;
            UnitSizes = UnitEnds.Select(e => e - ZeroPoint).ToArray();
        }

        public Option<TimeRange> TryGetUnit(TimeRange innerRange)
        {
            var result = TryGetUnit(innerRange.Start, innerRange.End);
            if (result.IsSome(out var some))
                return Option.Some<TimeRange>((some.Start, some.End));
            return Option.None<TimeRange>();
        }

        public TimeRange GetUnit(TimeRange innerRange)
            => GetUnit(innerRange.Start, innerRange.End);

        public bool IsValidUnit(TimeRange unit)
            => IsValidUnit(unit.Start, unit.End);
    }
}
