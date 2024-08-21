namespace ActualChat;

public static class DateTimeOffsetExt
{
    public static TimeSpan DelayTo(this DateTimeOffset utcNow, TimeSpan time, TimeZoneInfo timeZone)
    {
        var nowInUserTimeZone = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, timeZone);
        var nextInUserTimeZone = nowInUserTimeZone.Date.Add(time);
        if (nowInUserTimeZone >= nextInUserTimeZone)
            nextInUserTimeZone = nextInUserTimeZone.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextInUserTimeZone, timeZone);
        return new DateTimeOffset(nextUtc) - utcNow;
    }
}
