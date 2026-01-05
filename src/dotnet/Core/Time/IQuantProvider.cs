namespace ActualChat.Time;

public interface IQuantProvider
{
    public static virtual TimeSpan GetQuant(TimeSpan delay)
    {
        var delaySeconds = delay.TotalSeconds;
        if (delaySeconds >= 2 * 24 * 3600) // More than 2 days
            return TimeSpan.FromDays(1);
        if (delaySeconds >= 2 * 3600) // More than 2 hours
            return TimeSpan.FromHours(1);
        if (delaySeconds >= 2 * 60) // More than 2 minutes
            return TimeSpan.FromMinutes(1);
        return TimeSpan.FromSeconds(1); // We don't need to run flows more often than every second
    }
}
