using System.Linq;

namespace ActualChat.Mathematics.Internal
{
    public class ConvertingLogCover<TPoint, TSize> : LogCover<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public LogCover<double, double> BaseLogCover { get; }
        public ConvertingSizeMeasure<TPoint, TSize> ConvertingMeasure { get; }

        public ConvertingLogCover(
            LogCover<double, double> baseLogCover,
            ConvertingSizeMeasure<TPoint, TSize> convertingMeasure)
        {
            BaseLogCover = baseLogCover;
            ConvertingMeasure = convertingMeasure;
            Measure = convertingMeasure;
            Zero = ConvertingMeasure.PointFromDouble(BaseLogCover.Zero);
            MinTileSize = ConvertingMeasure.SizeFromDouble(BaseLogCover.MinTileSize);
            MaxTileSize = ConvertingMeasure.SizeFromDouble(BaseLogCover.MaxTileSize);
        }

        protected override TSize[] GetTileSizes()
            => BaseLogCover.TileSizes.Select(s => ConvertingMeasure.SizeFromDouble(s)).ToArray();
    }
}
