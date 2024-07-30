namespace ActualChat.Time;

public static class DeltaText
{
    public static (string Text, TimeSpan Delay) Get(DateTime time, DateTime now)
    {
        string result;
        TimeSpan delay;

        var delta = time - now;
        var isFuture = delta > TimeSpan.Zero;
        if (!isFuture)
            delta = TimeSpan.Zero - delta;

        if (delta.TotalSeconds <= 5)
            return ("just now", TimeSpan.FromSeconds(5) - delta);
        if (delta.TotalMinutes < 1)
            return (isFuture ? "in few seconds" : "few seconds ago", TimeSpan.FromMinutes(1) - delta);
        if (delta.TotalMinutes < 2)
            return (isFuture ? "in about 1 minute" : "a minute ago", TimeSpan.FromMinutes(2) - delta);
        if (delta.TotalMinutes < 5)
            return (isFuture ? "in few minutes" : "few minutes ago", TimeSpan.FromMinutes(5) - delta);

        if (delta < TimeSpan.FromMinutes(11)) {
            var minutes = (int)delta.TotalMinutes;
            result = isFuture ? $"in {minutes} minutes" : $"{minutes} minutes ago";
            delay = TimeSpan.FromMinutes(1).Multiply(minutes + 1) - delta;
            return (result, delay);
        }

        var localTimeDate = time.Date;
        var localToday = now.Date;
        if (localTimeDate == localToday) {
            result = $"today at {time.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - now.TimeOfDay;
        }
        else if (isFuture && localTimeDate == localToday.AddDays(1)) {
            result = $"tomorrow at {time.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - now.TimeOfDay;
        }
        else if (!isFuture && localTimeDate == localToday.AddDays(-1)) {
            result = $"yesterday at {time.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - now.TimeOfDay;
        }
        else {
            result = $"{time.ToShortDateString()} at {time.ToShortTimeString()}";
            delay = TimeSpan.MaxValue;
        }
        return (result, delay);
    }
}
