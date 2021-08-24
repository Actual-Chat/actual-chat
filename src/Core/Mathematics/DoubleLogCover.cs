using System;
using System.Collections.Generic;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics
{
    public sealed class DoubleLogCover : ILogCover<double, double>
    {
        public static ILogCover<double, double> Default { get; } = new DoubleLogCover();

        private double[]? _allSizes;

        public double Zero { get; init; } = 0;
        public double MinRangeSize { get; init; } = 1;
        public double MaxRangeSize { get; init; } = 1024 * 1024;
        public int RangeSizeFactor { get; init; } = 4;
        public double[] RangeSizes => _allSizes
            ??= Enumerable.Range(0, int.MaxValue)
                .Select(i => MinRangeSize * Math.Pow(RangeSizeFactor, i))
                .TakeWhile(size => size <= MaxRangeSize)
                .ToArray();
        public ISizeMeasure<double, double> SizeMeasure => DoubleSizeMeasure.Instance;
    }
}

