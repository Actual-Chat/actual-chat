using System.Linq;

namespace ActualChat.Mathematics
{
    public class ConvertingLogCover<TPoint, TSize> : ILogCover<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public DoubleLogCover BaseCover { get; }
        public ConvertingSizeMeasure<TPoint, TSize> SizeMeasure { get; }

        public TPoint Zero { get; }
        public TSize MinRangeSize { get; }
        public TSize MaxRangeSize { get; }
        public TSize[] RangeSizes { get; }
        public int RangeSizeFactor => BaseCover.RangeSizeFactor;
        ISizeMeasure<TPoint, TSize> ILogCover<TPoint, TSize>.SizeMeasure => SizeMeasure;

        public ConvertingLogCover(
            DoubleLogCover baseCover,
            ConvertingSizeMeasure<TPoint, TSize> sizeMeasure)
        {
            BaseCover = baseCover;
            SizeMeasure = sizeMeasure;
            Zero = SizeMeasure.PointFromDouble(BaseCover.Zero);
            MinRangeSize = SizeMeasure.SizeFromDouble(BaseCover.MinRangeSize);
            MaxRangeSize = SizeMeasure.SizeFromDouble(BaseCover.MaxRangeSize);
            RangeSizes = BaseCover.RangeSizes.Select(s => SizeMeasure.SizeFromDouble(s)).ToArray();
        }
    }

    public static class ConvertingLogCover
    {
        public static ConvertingLogCover<TPoint, TSize> New<TPoint, TSize>(
            DoubleLogCover baseCover,
            ConvertingSizeMeasure<TPoint, TSize> sizeMeasure)
            where TPoint : notnull
            where TSize : notnull
            => new(baseCover, sizeMeasure);
    }
}
