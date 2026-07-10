using ActualChat.Hosting;
using ActualChat.Time;

namespace ActualChat.UI.Blazor.Services;

public class LiveTime : SafeAsyncDisposableBase, IComputeService
{
    private static readonly TimeSpan RemainingRoundingTolerance = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _maxInvalidationDelay;

    private HostInfo HostInfo { get; }
    private DateTimeConverter DateTimeConverter { get; }
    private MomentClockSet Clocks { get; }

    public LiveTime(IServiceProvider services)
    {
        HostInfo = services.HostInfo();
        DateTimeConverter = services.GetRequiredService<DateTimeConverter>();
        Clocks = services.Clocks();
        _maxInvalidationDelay = HostInfo.IsDevelopmentInstance
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromMinutes(HostInfo.HostKind.IsApp() ? 10 : 5);
    }

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual Task<string> GetDeltaText(Moment time, CancellationToken cancellationToken)
    {
        var (text, delay) = GetDeltaTextInternal(time);
        if (delay < TimeSpan.MaxValue) {
            // Invalidate the result when it's supposed to change
            delay = TrimInvalidationDelay(delay + TimeSpan.FromMilliseconds(100));
            Computed.GetCurrent().Invalidate(delay, false);
        }
        return Task.FromResult(text);
    }

    [ComputeMethod]
    public virtual Task<string> GetRemainingText(Moment time, TimeSpan updatePeriod, CancellationToken cancellationToken)
    {
        var remaining = time - Clocks.ServerClock.Now;
        if (remaining <= TimeSpan.Zero)
            return Task.FromResult("");

        // Re-evaluate periodically so the countdown ticks down.
        Computed.GetCurrent().Invalidate(updatePeriod, false);
        var minutes = (int)Math.Ceiling((remaining - RemainingRoundingTolerance).TotalMinutes);
        var text = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
            : $"{Math.Max(1, minutes)}m";
        return Task.FromResult(text);
    }

    public string GetDeltaText(Moment time)
        => GetDeltaTextInternal(time).Text;

    // Private methods

    private (string Text, TimeSpan Delay) GetDeltaTextInternal(DateTime time)
    {
        var localTime = DateTimeConverter.ToLocalTime(time);
        var localNow = DateTimeConverter.ToLocalTime(Clocks.SystemClock.Now);
        return DeltaText.Get(localTime, localNow);
    }

    private TimeSpan TrimInvalidationDelay(TimeSpan delay)
        => TimeSpanExt.Min(delay, _maxInvalidationDelay);
}
