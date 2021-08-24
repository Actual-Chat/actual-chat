using System;
using Stl.Time;

namespace ActualChat.Mathematics.Internal
{
    public sealed class MomentLogCover : ConvertingLogCover<Moment, TimeSpan>
    {
        public MomentLogCover()
            : this(new DoubleLogCover() {
                Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
                MinRangeSize = TimeSpan.FromMinutes(3).TotalSeconds,
                MaxRangeSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
                RangeSizeFactor = 4,
            })
        { }

        public MomentLogCover(LogCover<double, double> baseLogCover)
            : base(
                baseLogCover,
                SizeMeasure.New(
                    m => m.ToUnixEpoch(),
                    s => new Moment(TimeSpan.FromSeconds(s)),
                    ts => ts.TotalSeconds,
                    TimeSpan.FromSeconds)
                )
        { }
    }
}
