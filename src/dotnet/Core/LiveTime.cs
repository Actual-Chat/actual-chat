namespace ActualChat;

public interface ILiveTime
{
    [ComputeMethod]
    Task<string> GetMomentsAgo(DateTime time);
}

internal class LiveTime : ILiveTime
{
    private static readonly TimeSpan MaxInvalidationDelay = TimeSpan.FromMinutes(10);

    public virtual Task<string> GetMomentsAgo(DateTime time)
    {
        var (result, delay) = GetText(time);
        if (delay < TimeSpan.MaxValue) {
            // Invalidate the result when it's supposed to change
            delay = TrimInvalidationDelay(delay + TimeSpan.FromMilliseconds(100));
            Computed.GetCurrent()!.Invalidate(delay, false);
        }
        return Task.FromResult(result);
    }

    private (string moment, TimeSpan delay) GetText(DateTime time)
    {
        string result;
        TimeSpan delay;

        var delta = DateTime.UtcNow - time.ToUniversalTime();
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta.TotalSeconds <= 5)
            return ("just now", TimeSpan.FromSeconds(5) - delta);
        if (delta.TotalMinutes < 1)
            return ("few seconds ago", TimeSpan.FromMinutes(1) - delta);
        if (delta.TotalMinutes < 2)
            return ("a minute ago", TimeSpan.FromMinutes(2) - delta);
        if (delta.TotalMinutes < 5)
            return ("few minutes ago", TimeSpan.FromMinutes(5) - delta);
        if (delta < TimeSpan.FromMinutes(11)) {
            var minutes = (int)delta.TotalMinutes;
            result = $"{minutes} minutes ago";
            delay = TimeSpan.FromMinutes(1).Multiply(minutes + 1) - delta;
            return (result, delay);
        }

        var now = DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        if (time.Date == today) {
            result = $"today, {time.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - now.TimeOfDay;
        }
        else if (time.Date == yesterday) {
            result = $"yesterday, {time.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - now.TimeOfDay;
        }
        else {
            result = $"{time.ToShortDateString()}, {time.ToShortTimeString()}";
            delay = TimeSpan.MaxValue;
        }
        return (result, delay);
    }

    private TimeSpan TrimInvalidationDelay(TimeSpan delay)
        => TimeSpanExt.Min(delay, MaxInvalidationDelay);
}

