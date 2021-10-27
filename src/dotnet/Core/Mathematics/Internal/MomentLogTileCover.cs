namespace ActualChat.Mathematics.Internal;

public sealed class MomentLogTileCover : ConvertingLogTileCover<Moment, TimeSpan>
{
    public MomentLogTileCover()
        : this(new LongLogTileCover {
            Zero = new DateTime(
                2020,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc).Ticks,
            MinTileSize = TimeSpan.FromMinutes(3).Ticks,
            MaxTileSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).Ticks, // ~ almost 6 years
            TileSizeFactor = 4,
        })
    { }

    public MomentLogTileCover(LogTileCover<long, long> baseTiles)
        : base(
            baseTiles,
            SizeMeasure.New(
                m => m.EpochOffsetTicks,
                ticks => new Moment(TimeSpan.FromTicks(ticks)),
                ts => ts.Ticks,
                TimeSpan.FromTicks)
        )
    { }
}
