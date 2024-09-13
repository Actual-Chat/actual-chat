namespace ActualChat;

public static class TimeZoneInfoExt
{
    public static DateTime ToDateTime(this Moment moment, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeFromUtc(moment.ToDateTime(), timeZone);

    public static Moment ToMoment(this DateTime dateTime, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeToUtc(dateTime, timeZone).ToMoment();

    // The time it returns always >= now
    public static Moment NextTimeOfDay(this TimeZoneInfo timeZone, TimeSpan timeOfDay, Moment now)
    {
        var localNow = now.ToDateTime(timeZone);
        var localResult = localNow.Date.Add(timeOfDay);
        if (localResult < localNow)
            localResult += TimeSpan.FromDays(1);
        return localResult.ToMoment(timeZone);
    }
}
