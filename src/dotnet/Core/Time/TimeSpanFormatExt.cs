namespace ActualChat.Time;

/// <summary>
/// Extension methods for formatting <see cref="TimeSpan"/> values.
/// </summary>
public static class TimeSpanFormatExt
{
    public static string Format(this TimeSpan value, string format)
        => format switch {
            "Default" => FormatDefault(value),
            "Short" => value.ToShortString(),
            "Clock" => FormatClock(value),
            "Seconds" => FormatSeconds(value),
            _ => value.ToString(format),
        };

    public static string Format(this TimeSpan value, TimeSpanFormat format = TimeSpanFormat.Default)
        => format switch {
            TimeSpanFormat.Default => FormatDefault(value),
            TimeSpanFormat.Short => value.ToShortString(),
            TimeSpanFormat.Clock => FormatClock(value),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

    // The largest unit the period divides into, in whole minutes at worst
    public static (int Count, PeriodUnit Unit) ToPeriodUnits(this TimeSpan value)
    {
        var minutes = (int)Math.Round(Math.Abs(value.TotalMinutes));
        if (minutes == 0 || minutes % 60 != 0)
            return (minutes, PeriodUnit.Minutes);

        var hours = minutes / 60;
        return hours % 24 == 0 ? (hours / 24, PeriodUnit.Days) : (hours, PeriodUnit.Hours);
    }

    // Rounded up to whole minutes, hours or days - whichever reads best for its length
    public static TimeSpan CeilingToPeriodUnit(this TimeSpan value)
        => value.TotalHours switch {
            < 1 => TimeSpan.FromMinutes(Math.Max(1, Math.Ceiling(value.TotalMinutes))),
            < 48 => TimeSpan.FromHours(Math.Ceiling(value.TotalHours)),
            _ => TimeSpan.FromDays(Math.Ceiling(value.TotalDays)),
        };

    public static TimeSpan ToTimeSpan(this PeriodUnit unit, long count)
        => unit switch {
            PeriodUnit.Days => TimeSpan.FromDays(count),
            PeriodUnit.Hours => TimeSpan.FromHours(count),
            _ => TimeSpan.FromMinutes(count),
        };

    // Private methods

    // Bare whole seconds, no unit — the caller appends a localized one
    private static string FormatSeconds(TimeSpan value)
        => $"{(int)Math.Abs(value.TotalSeconds)}";

    private static string FormatClock(TimeSpan value)
    {
        value = TimeSpan.FromTicks(Math.Abs(value.Ticks));
        var totalHours = (int)value.TotalHours;
        return totalHours > 0
            ? $"{totalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes}:{value.Seconds:D2}";
    }

    private static string FormatDefault(TimeSpan value)
    {
        value = TimeSpan.FromTicks(Math.Abs(value.Ticks));
        var (d, h, m, s) = (value.Days, value.Hours, value.Minutes, value.Seconds);
        if (d > 0)
            return $"{d} {"day".Pluralize(d)}, {h:D}:{m:D2}:{s:D2}";
        if (h > 0)
            return $"{h:D}:{m:D2}:{s:D2}";
        if (m > 0)
            return $"{m:D}:{s:D2}";
        return $"{s:D}s";
    }
}
