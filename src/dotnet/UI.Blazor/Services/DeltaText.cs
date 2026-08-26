using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Renders a time delta as live "5 minutes ago" text, paired with the delay after
/// which that text goes stale - see <see cref="LiveTime.GetDeltaText(Moment, CancellationToken)"/>.
/// </summary>
public sealed class DeltaText(IServiceProvider services)
{
    private DateFormatter DateFormatter => field ??= services.GetRequiredService<DateFormatter>();
    private IStringLocalizer L => field ??= services.GetRequiredService<IStringLocalizer>();

    public (string Text, TimeSpan Delay) Get(DateTime localTime, DateTime localNow)
    {
        var delta = localTime - localNow;
        var isFuture = delta > TimeSpan.Zero;
        if (!isFuture)
            delta = TimeSpan.Zero - delta;

        if (delta.TotalSeconds <= 5)
            return (L.LiveTime_JustNow, TimeSpan.FromSeconds(5) - delta);
        if (delta.TotalMinutes < 1)
            return (isFuture ? L.LiveTime_InFewSeconds : L.LiveTime_FewSecondsAgo,
                TimeSpan.FromMinutes(1) - delta);
        if (delta.TotalMinutes < 2)
            return (isFuture ? L.LiveTime_InAboutMinute : L.LiveTime_MinuteAgo,
                TimeSpan.FromMinutes(2) - delta);
        if (delta.TotalMinutes < 5)
            return (isFuture ? L.LiveTime_InFewMinutes : L.LiveTime_FewMinutesAgo,
                TimeSpan.FromMinutes(5) - delta);
        if (delta < TimeSpan.FromMinutes(11)) {
            var minutes = (int)delta.TotalMinutes;
            return (isFuture ? L.LiveTime_InMinutes_Format(minutes) : L.LiveTime_MinutesAgo_Format(minutes),
                TimeSpan.FromMinutes(1).Multiply(minutes + 1) - delta);
        }

        var date = localTime.Date;
        var today = localNow.Date;
        var untilTomorrow = TimeSpan.FromDays(1) - localNow.TimeOfDay;
        var time = localTime.ToString("t", DateFormatter);
        if (date == today)
            return (time, untilTomorrow);
        if (isFuture && date == today.AddDays(1))
            return (L.LiveTime_TomorrowAt_Format(time), untilTomorrow);
        if (!isFuture && date == today.AddDays(-1))
            return (L.LiveTime_YesterdayAt_Format(time), untilTomorrow);

        return (L.Date_At_Format(localTime.ToString("d", DateFormatter), time), TimeSpan.MaxValue);
    }
}
