using System;
using System.Collections.Generic;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics
{
    public sealed class LongLogCover : ILogCover<long, long>
    {
        public static LongLogCover Default { get; } = new();

        private long[]? _allSizes;

        public long Zero { get; init; } = 0;
        public long MinSpanSize { get; init; } = 16;
        public long MaxSpanSize { get; init; } = 16_384;
        public int SpanSizeMultiplier { get; init; } = 4;
        public long[] SpanSizes => _allSizes
            ??= Enumerable.Range(0, int.MaxValue)
                .Select(i => MinSpanSize * (long) Math.Pow(SpanSizeMultiplier, i))
                .TakeWhile(size => size <= MaxSpanSize)
                .ToArray();


        public bool IsValidSpan(LongRange span)
        {
            var size = span.Size;
            for (var i = 0; i < SpanSizes.Length; i++) {
                var unitSize = SpanSizes[i];
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (unitSize == size) {
                    var offset = (span.Start - Zero) % size;
                    return offset == 0;
                }
            }
            return false;
        }

        public long GetSpanStart(long innerPoint, int spanSizeIndex)
        {
            var size = SpanSizes[spanSizeIndex];
            var offset = (innerPoint - Zero) % size;
            return innerPoint - offset;
        }

        public IEnumerable<LongRange> GetSpans(long innerPoint)
        {
            foreach (var size in SpanSizes) {
                var offset = (innerPoint - Zero) % size;
                var start = innerPoint - offset;
                yield return (start, start + size);
            }
        }

        public Option<LongRange> TryGetSpan(LongRange innerSpan)
        {
            if (innerSpan.Start > innerSpan.End)
                throw new ArgumentOutOfRangeException(nameof(innerSpan));
            var minSize = innerSpan.Size;
            // TODO(AY): Use binary search when (AllSizes.Length >= 8) or so
            for (var i = 0; i < SpanSizes.Length; i++) {
                var size = SpanSizes[i];
                if (size > minSize) {
                    var start = GetSpanStart(innerSpan.Start, i);
                    if (start + size >= innerSpan.End)
                        return Option.Some(new LongRange(start, start + size));
                }
            }
            return Option.None<LongRange>();
        }

        public LongRange GetSpan(LongRange innerSpan)
            => TryGetSpan(innerSpan).IsSome(out var value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(innerSpan));

        // Explicit implementation of ILogCover<long, long>

        bool ILogCover<long, long>.IsValidSpan(long start, long end)
            => IsValidSpan((start, end));

        IEnumerable<(long Start, long End)> ILogCover<long, long>.GetSpans(long innerPoint)
            => GetSpans(innerPoint).Select(span => (span.Start, span.End));

        Option<(long Start, long End)> ILogCover<long, long>.TryGetSpan(long maxStart, long minEnd)
        {
            var result = TryGetSpan((maxStart, minEnd));
            return result.IsSome(out var span)
                ? Option.Some((span.Start, span.End))
                : Option.None<(long, long)>();
        }

        (long Min, long Max) ILogCover<long, long>.GetSpan(long maxStart, long minEnd)
            => TryGetSpan((maxStart, minEnd)).IsSome(out var value)
                ? (value.Start, value.End)
                : throw new ArgumentOutOfRangeException(nameof(minEnd));
    }
}
