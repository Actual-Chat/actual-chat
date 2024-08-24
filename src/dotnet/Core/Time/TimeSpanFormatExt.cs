namespace ActualChat.Time;

public enum TimeSpanFormat
{
    Default = 0,
    Short,
}

public static class TimeSpanFormatExt
{
    public static string Format(this TimeSpan value, string format)
        => format switch {
            "Default" => FormatDefault(value),
            "Short" => value.ToShortString(),
            _ => value.ToString(format, CultureInfo.InvariantCulture),
        };

    public static string Format(this TimeSpan value, TimeSpanFormat format = TimeSpanFormat.Default)
        => format switch {
            TimeSpanFormat.Default => FormatDefault(value),
            TimeSpanFormat.Short => value.ToShortString(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

    // Private methods

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
