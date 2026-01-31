namespace ActualChat.Time;

public static class AutoDelayQuanta
{
    public static readonly TimeSpan DefaultMinQuanta = TimeSpan.FromSeconds(0.1);

    public static TimeSpan For(TimeSpan delay)
        => For(delay, DefaultMinQuanta);

    public static TimeSpan For(TimeSpan delay, TimeSpan minQuanta)
    {
        var seconds = (long)Math.Floor(delay.TotalSeconds);
        return seconds switch {
            >= 3 * 24 * 3600 => TimeSpan.FromDays(1), // When 3+ days
            >= 3 * 3600 => TimeSpan.FromHours(1), // 3+ hours
            >= 30 * 60 => TimeSpan.FromMinutes(10), // 30+ minutes
            >= 3 * 60 => TimeSpan.FromMinutes(1), // 3+ minutes
            >= 30 => TimeSpan.FromSeconds(10), // More than 30 seconds
            >= 3 => TimeSpan.FromSeconds(1), // More than 3 seconds
            _ => minQuanta,
        };
    }
}
