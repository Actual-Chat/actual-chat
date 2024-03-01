namespace ActualChat;

public static class MomentExt
{
    public static Moment? Convert(this Moment? moment, IMomentClock fromClock, IMomentClock toClock)
        => moment is { } v ? v.Convert(fromClock, toClock) : null;

    public static Moment Convert(this Moment moment, IMomentClock fromClock, IMomentClock toClock)
    {
        var offset = moment - fromClock.Now;
        return toClock.Now + offset;
    }

    public static Moment ToLastIntervalStart(this Moment moment, TimeSpan interval)
    {
        var d = moment.ToDateTimeOffset();
        var ticksSinceLastInterval = d.Ticks % interval.Ticks;
        return moment - TimeSpan.FromTicks(ticksSinceLastInterval);
    }
}
