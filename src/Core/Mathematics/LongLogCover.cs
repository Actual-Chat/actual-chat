using System;
using System.Collections.Generic;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics
{
    public sealed class LongLogCover : ILogCover<long, long>
    {
        public static ILogCover<long, long> Default { get; } = new LongLogCover();

        private long[]? _allSizes;

        public long Zero { get; init; } = 0;
        public long MinRangeSize { get; init; } = 16;
        public long MaxRangeSize { get; init; } = 16_384;
        public int RangeSizeFactor { get; init; } = 4;
        public long[] RangeSizes => _allSizes
            ??= Enumerable.Range(0, int.MaxValue)
                .Select(i => MinRangeSize * (long) Math.Pow(RangeSizeFactor, i))
                .TakeWhile(size => size <= MaxRangeSize)
                .ToArray();
        public ISizeMeasure<long, long> SizeMeasure => LongSizeMeasure.Instance;
    }
}
