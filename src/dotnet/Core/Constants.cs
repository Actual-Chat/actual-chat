namespace ActualChat;

public static class Constants
{
    public static class Time
    {
        public static readonly Moment Inf = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToMoment();
    }

    public static class LongId
    {
        public static readonly long Inf = long.MaxValue;
    }
}
