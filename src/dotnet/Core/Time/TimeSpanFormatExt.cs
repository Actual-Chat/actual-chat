namespace ActualChat.Time;

/// <summary>
/// Specifies the format for displaying time span values.
/// </summary>
public enum TimeSpanFormat
{
    Default = 0,
    Short,
    // m:ss, switching to h:mm:ss past an hour — media-player style (e.g. 0:05, not 5s)
    Clock,
}

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
            _ => value.ToString(format),
        };

    public static string Format(this TimeSpan value, TimeSpanFormat format = TimeSpanFormat.Default)
        => format switch {
            TimeSpanFormat.Default => FormatDefault(value),
            TimeSpanFormat.Short => value.ToShortString(),
            TimeSpanFormat.Clock => FormatClock(value),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

    // Private methods

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
