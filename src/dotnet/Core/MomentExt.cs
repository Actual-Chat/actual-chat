namespace ActualChat;

public static class MomentExt
{
    public static long ToVersion(this Moment moment)
        => moment.EpochOffsetTicks;

    public static long ToVersion(this Moment moment, TimeSpan offset)
        => (moment + offset).EpochOffsetTicks;
}
