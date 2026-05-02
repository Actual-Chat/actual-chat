namespace ActualChat;

public static class MomentExt
{
    public static long ToVersion(this Moment moment)
        => moment.EpochOffsetTicks;

    public static long ToVersion(this Moment moment, TimeSpan offset)
        => (moment + offset).EpochOffsetTicks;

    public static Moment Max(Moment moment1, Moment moment2, Moment moment3)
        => Moment.Max(moment1, Moment.Max(moment2, moment3));
}
