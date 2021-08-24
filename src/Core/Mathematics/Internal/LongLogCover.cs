using System;
using System.Linq;

namespace ActualChat.Mathematics.Internal
{
    public sealed class LongLogCover : LogCover<long, long>
    {
        public LongLogCover()
        {
            MinRangeSize = 16;
            MaxRangeSize = 16_384;
            Measure = SizeMeasure.Long;
        }

        protected override long[] GetRangeSizes()
            => Enumerable.Range(0, int.MaxValue)
                .Select(i => MinRangeSize * (long) Math.Pow(RangeSizeFactor, i))
                .TakeWhile(size => size <= MaxRangeSize)
                .ToArray();
    }
}
