using System;
using System.Collections.Generic;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics
{
    public class ConvertingLogCover<TPoint, TSize> : ILogCover<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public DoubleLogCover BaseCover { get; }
        public Func<TPoint, double> PointToBaseConverter { get; }
        public Func<TSize, double> SizeToBaseConverter { get; }
        public Func<double, TPoint> PointFromBaseConverter { get; }
        public Func<double, TSize> SizeFromBaseConverter { get; }

        public TPoint Zero { get; }
        public TSize MinSpanSize { get; }
        public TSize MaxSpanSize { get; }
        public TSize[] SpanSizes { get; }
        public int SpanSizeMultiplier => BaseCover.SpanSizeMultiplier;

        public ConvertingLogCover(
            DoubleLogCover baseCover,
            Func<TPoint, double> pointToBaseConverter,
            Func<TSize, double> sizeToBaseConverter,
            Func<double, TPoint> pointFromBaseConverter,
            Func<double, TSize> sizeFromBaseConverter)
        {
            BaseCover = baseCover;
            PointToBaseConverter = pointToBaseConverter;
            SizeToBaseConverter = sizeToBaseConverter;
            PointFromBaseConverter = pointFromBaseConverter;
            SizeFromBaseConverter = sizeFromBaseConverter;
            Zero = PointFromBaseConverter(BaseCover.Zero);
            MinSpanSize = SizeFromBaseConverter(BaseCover.MinSpanSize);
            MaxSpanSize = SizeFromBaseConverter(BaseCover.MaxSpanSize);
            SpanSizes = BaseCover.SpanSizes.Select(s => SizeFromBaseConverter(s)).ToArray();
        }

        public bool IsValidSpan(TPoint start, TPoint end)
            => BaseCover.IsValidSpan(PointToBaseConverter(start), PointToBaseConverter(end));

        public TPoint GetSpanStart(TPoint innerPoint, int spanSizeIndex)
            => PointFromBaseConverter(BaseCover.GetSpanStart(PointToBaseConverter(innerPoint), spanSizeIndex));

        public IEnumerable<(TPoint Start, TPoint End)> GetSpans(TPoint innerPoint)
            => BaseCover
                .GetSpans(PointToBaseConverter(innerPoint))
                .Select(span => (PointFromBaseConverter(span.Start), PointFromBaseConverter(span.End)));

        public Option<(TPoint Start, TPoint End)> TryGetSpan(TPoint maxStart, TPoint minEnd)
        {
            var result = BaseCover.TryGetSpan(PointToBaseConverter(maxStart), PointToBaseConverter(minEnd));
            if (result.IsSome(out var some))
                return Option.Some((PointFromBaseConverter(some.Start), PointFromBaseConverter(some.End)));
            return Option.None<(TPoint, TPoint)>();
        }

        public (TPoint Min, TPoint Max) GetSpan(TPoint maxStart, TPoint minEnd)
        {
            var (start, end) = BaseCover.GetSpan(PointToBaseConverter(maxStart), PointToBaseConverter(minEnd));
            return (PointFromBaseConverter(start), PointFromBaseConverter(end));
        }
    }

    public static class ConvertingLogCover
    {
        public static ConvertingLogCover<TPoint, TSize> New<TPoint, TSize>(
            DoubleLogCover baseCover,
            Func<TPoint, double> pointToBaseConverter,
            Func<TSize, double> sizeToBaseConverter,
            Func<double, TPoint> pointFromBaseConverter,
            Func<double, TSize> sizeFromBaseConverter)
            where TPoint : notnull
            where TSize : notnull
            => new(baseCover, pointToBaseConverter, sizeToBaseConverter, pointFromBaseConverter, sizeFromBaseConverter);
    }
}
