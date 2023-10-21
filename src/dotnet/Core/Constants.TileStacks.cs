namespace ActualChat;

public static partial class Constants
{
    public static class TileStacks
    {
        public static readonly TileStack<long> Long16 = new(0L, 16L);
        public static readonly TileStack<long> Long16To1K = new(0L, 16L, 1024L, 4);
        public static readonly TileStack<long> Long5To1K = new(0L, 5L, 1280L, 4);
        public static readonly TileStack<Moment> Moment3MTo6Y = new(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new Moment(TimeSpan.FromMinutes(3)),
            new Moment(TimeSpan.FromMinutes(3 * Math.Pow(4, 10))), // ~ almost 6 years
            4);
    }
}
