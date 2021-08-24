using System;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public static class RangeEx
    {
        public static long Size(this Range<long> range)
            => range.End - range.Start;

        public static double Size(this Range<double> range)
            => range.End - range.Start;

        public static TimeSpan Size(this Range<Moment> range)
            => range.End - range.Start;

        public static TimeSpan Size(this Range<DateTime> range)
            => range.End - range.Start;
    }
}
