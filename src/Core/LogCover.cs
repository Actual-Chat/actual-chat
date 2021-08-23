using System;
using System.Linq;
using Stl;

namespace ActualChat
{
    public sealed record LogCover
    {
        public static LogCover Default { get; } = new();

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

        public static LogCover New(double zero, double minSpanSize, double maxSpanSize, int spanSizeMultiplier = 4)
            => new() {
                Zero = zero,
                MinSpanSize = minSpanSize,
                MaxSpanSize = maxSpanSize,
                SpanSizeMultiplier = spanSizeMultiplier,
            };

        public static LogCover<T> New<T>(
            LogCover baseCover,
            Func<T, double> toBaseConverter,
            Func<double, T> fromBaseConverter)
            => new(baseCover, toBaseConverter, fromBaseConverter);

        public double GetSpanStart(double maxStart, int spanSizeIndex)
        {
            var size = SpanSizes[spanSizeIndex];
            var offset = (maxStart - Zero) % size;
            return maxStart - offset;
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
    }

    public class LogCover<T>
    {
        public LogCover BaseCover { get; }
        public Func<T, double> ToBaseConverter { get; }
        public Func<double, T> FromBaseConverter { get; }

        public T ZeroPoint { get; }
        public T MinSpanEnd { get; }
        public T MaxSpanEnd { get; }
        public T[] SpanEnds { get; }
        public int SpanSizeMultiplier => BaseCover.SpanSizeMultiplier;

        public LogCover(
            LogCover baseCover,
            Func<T, double> toBaseConverter,
            Func<double, T> fromBaseConverter)
        {
            BaseCover = baseCover;
            ToBaseConverter = toBaseConverter;
            FromBaseConverter = fromBaseConverter;
            ZeroPoint = FromBaseConverter(BaseCover.Zero);
            MinSpanEnd = FromBaseConverter(BaseCover.Zero + BaseCover.MinSpanSize);
            MaxSpanEnd = FromBaseConverter(BaseCover.Zero + BaseCover.MaxSpanSize);
            SpanEnds = BaseCover.SpanSizes.Select(s => FromBaseConverter(BaseCover.Zero + s)).ToArray();
        }

        public T GetSpanStart(T maxStart, int spanSizeIndex)
            => FromBaseConverter(BaseCover.GetSpanStart(ToBaseConverter(maxStart), spanSizeIndex));

        public Option<(T Start, T End)> TryGetSpan(T maxStart, T minEnd)
        {
            var result = BaseCover.TryGetSpan(ToBaseConverter(maxStart), ToBaseConverter(minEnd));
            if (result.IsSome(out var some))
                return Option.Some((FromBaseConverter(some.Start), FromBaseConverter(some.End)));
            return Option.None<(T Start, T End)>();
        }

        public (T Min, T Max) GetSpan(T maxStart, T minEnd)
        {
            var (start, end) = BaseCover.GetSpan(ToBaseConverter(maxStart), ToBaseConverter(minEnd));
            return (FromBaseConverter(start), FromBaseConverter(end));
        }

        public bool IsValidSpan(T start, T end)
            => BaseCover.IsValidSpan(ToBaseConverter(start), ToBaseConverter(end));
    }
}
