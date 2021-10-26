namespace ActualChat.Mathematics.Internal;

public sealed class MomentLogTileCover : ConvertingLogTileCover<Moment, TimeSpan>
{
    public MomentLogTileCover()
        : this(new DoubleLogTileCover() {
            Zero = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment().ToUnixEpoch(),
            MinTileSize = TimeSpan.FromMinutes(3).TotalSeconds,
            MaxTileSize = TimeSpan.FromMinutes(3 * Math.Pow(4, 10)).TotalSeconds, // ~ almost 6 years
            TileSizeFactor = 4,
        })
    { }

    public MomentLogTileCover(LogTileCover<double, double> baseTiles)
        : base(
            baseTiles,
            SizeMeasure.New(
                m => m.ToUnixEpoch(),
                s => new Moment(TimeSpan.FromSeconds(s)),
                ts => ts.TotalSeconds,
                TimeSpan.FromSeconds)
        )
    { }
}
