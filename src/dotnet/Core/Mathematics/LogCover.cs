using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

public static class LogCover
{
    public static ConvertingLogTileCover<TPoint, TSize> New<TPoint, TSize>(
        LongLogTileCover baseTiles,
        ConvertingSizeMeasure<TPoint, TSize> sizeMeasure)
        where TPoint : notnull
        where TSize : notnull
        => new (baseTiles, sizeMeasure);

    public static class Default
    {
        public static LogTileCover<long, long> Long { get; } = new LongLogTileCover();
        public static LogTileCover<double, double> Double { get; } = new DoubleLogTileCover();
        public static LogTileCover<Moment, TimeSpan> Moment { get; } = new MomentLogTileCover();
    }
}
