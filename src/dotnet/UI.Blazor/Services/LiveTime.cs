using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class LiveTime : SafeAsyncDisposableBase, IComputeService
{
    private readonly TimeSpan _maxInvalidationDelay;

    private HostInfo HostInfo { get; }
    private TimeZoneConverter TimeZoneConverter { get; }
    private MomentClockSet Clocks { get; }

    public LiveTime(IServiceProvider services)
    {
        HostInfo = services.HostInfo();
        TimeZoneConverter = services.GetRequiredService<TimeZoneConverter>();
        Clocks = services.Clocks();
        _maxInvalidationDelay = HostInfo.IsDevelopmentInstance
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromMinutes(HostInfo.AppKind.IsClient() ? 10 : 5);
    }

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual Task<string> GetDeltaText(Moment time, CancellationToken cancellationToken)
    {
        var (text, delay) = GetDeltaTextImpl(time, Clocks.SystemClock.Now);
        if (delay < TimeSpan.MaxValue) {
            // Invalidate the result when it's supposed to change
            delay = TrimInvalidationDelay(delay + TimeSpan.FromMilliseconds(100));
            Computed.GetCurrent()!.Invalidate(delay, false);
        }
        return Task.FromResult(text);
    }

    public string GetDeltaText(Moment time)
        => GetDeltaTextImpl(time, Clocks.SystemClock.Now).Text;

    public string GetDeltaText(Moment time, Moment now)
        => GetDeltaTextImpl(time, now).Text;

    // Private methods

    private (string Text, TimeSpan Delay) GetDeltaTextImpl(Moment time, Moment now)
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

        var localTime = TimeZoneConverter.ToLocalTime(time);
        var localTimeDate = localTime.Date;
        var localNow = TimeZoneConverter.ToLocalTime(now);
        var localToday = localNow.Date;
        if (localTimeDate == localToday) {
            result = $"today at {localTime.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - localNow.TimeOfDay;
        }
        else if (isFuture && localTimeDate == localToday.AddDays(1)) {
            result = $"tomorrow at {localTime.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - localNow.TimeOfDay;
        }
        else if (!isFuture && localTimeDate == localToday.AddDays(-1)) {
            result = $"yesterday at {localTime.ToShortTimeString()}";
            delay = TimeSpan.FromDays(1) - localNow.TimeOfDay;
        }
        else {
            result = $"{localTime.ToShortDateString()} at {localTime.ToShortTimeString()}";
            delay = TimeSpan.MaxValue;
        }
        return (result, delay);
    }

    private TimeSpan TrimInvalidationDelay(TimeSpan delay)
        => TimeSpanExt.Min(delay, _maxInvalidationDelay);
}
