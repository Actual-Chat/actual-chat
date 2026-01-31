namespace ActualChat.Time;

public static class AutoDelayQuanta
{
    public static TimeSpan For(TimeSpan delay)
    {
        var seconds = (long)Math.Floor(delay.TotalSeconds);
        return seconds switch {
            // We don't want quanta longer than 1h, coz they "combine" all events and fire them at once
            >= 5 * 3600 => TimeSpan.FromHours(1), // 5+ hours
            >= 30 * 60 => TimeSpan.FromMinutes(5), // 30+ minutes
            >= 5 * 60 => TimeSpan.FromMinutes(1), // 5+ minutes
            >= 30 => TimeSpan.FromSeconds(5),
            >= 5 => TimeSpan.FromSeconds(1),
            _ => TimeSpan.Zero, // No delay quanta
        };
    }
}
