using System;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public sealed class MomentLogCover : ConvertingLogCover<Moment, TimeSpan>
    {
        public static ILogCover<Moment, TimeSpan> Default { get; } =
            new MomentLogCover(
                new DoubleLogCover() {
                    Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
                    MinRangeSize = TimeSpan.FromMinutes(3).TotalSeconds,
                    MaxRangeSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
                    RangeSizeFactor = 4,
                });

        public MomentLogCover(DoubleLogCover baseCover)
            : base(
                baseCover,
                new ConvertingSizeMeasure<Moment, TimeSpan>(
                    m => m.ToUnixEpoch(),
                    s => new Moment(TimeSpan.FromSeconds(s)),
                    ts => ts.TotalSeconds,
                    TimeSpan.FromSeconds)
                )
        { }
    }
}
