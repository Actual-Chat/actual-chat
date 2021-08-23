using System;
using System.Collections.Generic;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics
{
    public sealed class DoubleLogCover : ILogCover<double, double>
    {
        public static DoubleLogCover Default { get; } = new();

        private double[]? _allSizes;

        public double Zero { get; init; } = 0;
        public double MinSpanSize { get; init; } = 1;
        public double MaxSpanSize { get; init; } = 1024 * 1024;
        public int SpanSizeMultiplier { get; init; } = 4;
        public double[] SpanSizes => _allSizes
            ??= Enumerable.Range(0, int.MaxValue)
                .Select(i => MinSpanSize * Math.Pow(SpanSizeMultiplier, i))
                .TakeWhile(size => size <= MaxSpanSize)
                .ToArray();

        public bool IsValidSpan(double start, double end)
        {
            var size = end - start;
            for (var i = 0; i < SpanSizes.Length; i++) {
                var unitSize = SpanSizes[i];
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (unitSize == size) {
                    var offset = (start - Zero) % size;
                    return offset == 0;
                }
            }
            return false;
        }

        public double GetSpanStart(double innerPoint, int spanSizeIndex)
        {
            var size = SpanSizes[spanSizeIndex];
            var offset = (innerPoint - Zero) % size;
            return innerPoint - offset;
        }

        public IEnumerable<(double Start, double End)> GetSpans(double innerPoint)
        {
            foreach (var size in SpanSizes) {
                var offset = (innerPoint - Zero) % size;
                var start = innerPoint - offset;
                yield return (start, start + size);
            }
        }

        public Option<(double Start, double End)> TryGetSpan(double maxStart, double minEnd)
        {
            if (maxStart > minEnd)
                throw new ArgumentOutOfRangeException(nameof(minEnd));
            var minSize = minEnd - maxStart;
            // TODO(AY): Use binary search when (AllSizes.Length >= 8) or so
            for (var i = 0; i < SpanSizes.Length; i++) {
                var size = SpanSizes[i];
                if (size > minSize) {
                    var start = GetSpanStart(maxStart, i);
                    if (start + size >= minEnd)
                        return (start, start + size);
                }
            }
            return Option.None<(double, double)>();
        }

        public (double Min, double Max) GetSpan(double maxStart, double minEnd)
            => TryGetSpan(maxStart, minEnd).IsSome(out var value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(minEnd));
    }
}
