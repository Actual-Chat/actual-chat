using System;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics.Internal
{
    public sealed class DoubleLogCover : LogCover<double, double>
    {
        public DoubleLogCover()
        {
            MinRangeSize = 1;
            MaxRangeSize = 1024 * 1024;
            Measure = SizeMeasure.Double;
        }

        protected override double[] GetRangeSizes()
            => Enumerable.Range(0, int.MaxValue)
                .Select(i => MinRangeSize * Math.Pow(RangeSizeFactor, i))
                .TakeWhile(size => size <= MaxRangeSize)
                .ToArray();
    }
}

