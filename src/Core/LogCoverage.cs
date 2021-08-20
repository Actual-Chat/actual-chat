using System;
using System.Linq;
using Stl;

namespace ActualChat
{
    public record LogCoverage
    {
        public static LogCoverage Default { get; } = new();

        private double[]? _allSizes;

        public double Zero { get; init; } = 0;
        public double MinUnitSize { get; init; } = 1;
        public double MaxUnitSize { get; init; } = 1024 * 1024;
        public double UnitSizeMultiplier { get; init; } = 4;
        public double[] UnitSizes => _allSizes
            ??= Enumerable.Range(0, int.MaxValue)
                .Select(i => MinUnitSize * Math.Pow(UnitSizeMultiplier, i))
                .TakeWhile(size => size <= MaxUnitSize)
                .ToArray();

        public static LogCoverage New(double zero, double minUnitSize, double maxUnitSize, double unitSizeMultiplier = 4)
            => new() {
                Zero = zero,
                MinUnitSize = minUnitSize,
                MaxUnitSize = maxUnitSize,
                UnitSizeMultiplier = unitSizeMultiplier,
            };

        public static LogCoverage<T> New<T>(
            LogCoverage baseCoverage,
            Func<T, double> toBaseConverter,
            Func<double, T> fromBaseConverter)
            => new(baseCoverage, toBaseConverter, fromBaseConverter);

        public double GetUnitStart(double maxStart, int unitSizeIndex)
        {
            var size = UnitSizes[unitSizeIndex];
            var offset = (maxStart - Zero) % size;
            return maxStart - offset;
        }

        public Option<(double Start, double End)> TryGetUnit(double maxStart, double minEnd)
        {
            if (maxStart > minEnd)
                throw new ArgumentOutOfRangeException(nameof(minEnd));
            var minSize = minEnd - maxStart;
            // TODO(AY): Use binary search when (AllSizes.Length >= 8) or so
            for (var i = 0; i < UnitSizes.Length; i++) {
                var size = UnitSizes[i];
                if (size > minSize) {
                    var start = GetUnitStart(maxStart, i);
                    if (start + size >= minEnd)
                        return (start, start + size);
                }
            }
            return Option.None<(double, double)>();
        }

        public (double Min, double Max) GetUnit(double maxStart, double minEnd)
            => TryGetUnit(maxStart, minEnd).IsSome(out var value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(minEnd));

        public bool IsValidUnit(double start, double end)
        {
            var size = end - start;
            for (var i = 0; i < UnitSizes.Length; i++) {
                var unitSize = UnitSizes[i];
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (unitSize == size) {
                    var offset = (start - Zero) % size;
                    return offset == 0;
                }
            }
            return false;
        }
    }

    public class LogCoverage<T>
    {
        public LogCoverage BaseCoverage { get; }
        public Func<T, double> ToBaseConverter { get; }
        public Func<double, T> FromBaseConverter { get; }

        public T ZeroPoint { get; }
        public T MinUnitEnd { get; }
        public T MaxUnitEnd { get; }
        public T[] UnitEnds { get; }
        public double UnitSizeMultiplier => BaseCoverage.UnitSizeMultiplier;

        public LogCoverage(
            LogCoverage baseCoverage,
            Func<T, double> toBaseConverter,
            Func<double, T> fromBaseConverter)
        {
            BaseCoverage = baseCoverage;
            ToBaseConverter = toBaseConverter;
            FromBaseConverter = fromBaseConverter;
            ZeroPoint = FromBaseConverter(BaseCoverage.Zero);
            MinUnitEnd = FromBaseConverter(BaseCoverage.Zero + BaseCoverage.MinUnitSize);
            MaxUnitEnd = FromBaseConverter(BaseCoverage.Zero + BaseCoverage.MaxUnitSize);
            UnitEnds = BaseCoverage.UnitSizes.Select(s => FromBaseConverter(BaseCoverage.Zero + s)).ToArray();
        }

        public T GetUnitStart(T maxStart, int unitSizeIndex)
            => FromBaseConverter(BaseCoverage.GetUnitStart(ToBaseConverter(maxStart), unitSizeIndex));

        public Option<(T Start, T End)> TryGetUnit(T maxStart, T minEnd)
        {
            var result = BaseCoverage.TryGetUnit(ToBaseConverter(maxStart), ToBaseConverter(minEnd));
            if (result.IsSome(out var some))
                return Option.Some((FromBaseConverter(some.Start), FromBaseConverter(some.End)));
            return Option.None<(T Start, T End)>();
        }

        public (T Min, T Max) GetUnit(T maxStart, T minEnd)
        {
            var (start, end) = BaseCoverage.GetUnit(ToBaseConverter(maxStart), ToBaseConverter(minEnd));
            return (FromBaseConverter(start), FromBaseConverter(end));
        }

        public bool IsValidUnit(T start, T end)
            => BaseCoverage.IsValidUnit(ToBaseConverter(start), ToBaseConverter(end));
    }
}
