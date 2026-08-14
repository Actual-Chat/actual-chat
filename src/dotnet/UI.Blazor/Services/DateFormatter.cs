using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The UI language's <see cref="DateTimeFormatInfo"/>, usable anywhere a
/// <see cref="CultureInfo"/> would be: <c>localTime.ToString("D", DateFormatter)</c>.
/// The catalog fills the standard slots, so the shapes the UI has are
/// "t" time, "m" month-day, "d" short date, "D" full date, "y" year-month.
/// </summary>
public sealed class DateFormatter(IServiceProvider services) : IFormatProvider
{
    private (Language Language, DateTimeFormatInfo Info)? _formats;

    private IStringLocalizer L => field ??= services.GetRequiredService<IStringLocalizer>();
    private DateTimeFormatInfo Formats {
        get {
            var language = ((IHasUILanguage)L).UILanguage;
            if (_formats is not { } formats || formats.Language != language)
                _formats = formats = (language, L.NewFormatInfo());
            return formats.Info;
        }
    }

    public object? GetFormat(Type? formatType)
        => formatType == typeof(DateTimeFormatInfo) ? Formats : null;

    public string FormatRelativeDate(DateTime localTime, DateTime localNow)
        // Today / Yesterday / Fri / Aug 14 / Aug 14, 2026 - the coarser the older
        => (localNow.Date - localTime.Date).Days switch {
            0 => L.Common_Today,
            1 => L.Common_Yesterday,
            < 8 and > 0 => localTime.ToString("ddd", Formats),
            _ => localTime.ToString(localTime.Year == localNow.Year ? "m" : "d", Formats),
        };

    public string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        // Anything below a minute still reads as "1 min" rather than "0 min"
        var extraMinute = duration.Seconds > 0 ? 1 : 0;
        if (duration.TotalMinutes < 1)
            return L.Duration_Minutes_Format(1);
        if (duration.TotalHours < 1)
            return L.Duration_Minutes_Format((int)duration.TotalMinutes + extraMinute);

        var parts = new List<string>(3);
        if (duration.TotalDays >= 1)
            parts.Add(L.Duration_Days_Format((int)duration.TotalDays));
        parts.Add(L.Duration_Hours_Format((int)duration.TotalHours % 24));
        parts.Add(L.Duration_Minutes_Format(duration.Minutes + extraMinute));
        return parts.ToDelimitedString(" ");
    }
}
