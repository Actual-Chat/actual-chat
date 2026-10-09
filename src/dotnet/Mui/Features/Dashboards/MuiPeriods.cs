namespace ActualChat.Mui;

public enum MuiInterval
{
    Day,
    Week,
}

// How far back each compared window is shifted: by a week, a month, a quarter, or by its own length
public enum MuiChartKind
{
    Bar,
    Line,
}

public enum MuiStep
{
    Week,
    Month,
    Quarter,
    Interval,
}

// Windows are rolling and end at "now", so they are comparable and always current:
// window 0 is the last interval, window k is the same span shifted back k steps.
public sealed record MuiPeriodSettings(
    MuiInterval Interval = MuiInterval.Day,
    MuiStep Step = MuiStep.Week,
    int Count = 1,
    MuiChartKind Chart = MuiChartKind.Bar)
{
    public const int MaxCount = 12;

    public TimeSpan Length => Interval == MuiInterval.Day ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);

    public MuiWindow[] GetWindows(DateTime now)
    {
        var count = Math.Clamp(Count, 1, MaxCount);
        var windows = new MuiWindow[count + 1];
        for (var k = 0; k <= count; k++) {
            var to = Shift(now, k);
            windows[k] = new MuiWindow(k, to - Length, to, GetLabel(k), GetShortLabel(k));
        }
        return windows;
    }

    public string ToStorageString()
        => $"{Interval}|{Step}|{Count}|{Chart}";

    public static MuiPeriodSettings FromStorageString(string? text)
    {
        var parts = (text ?? "").Split('|');
        if (parts.Length is >= 3 and <= 5
            && Enum.TryParse<MuiInterval>(parts[0], out var interval)
            && Enum.TryParse<MuiStep>(parts[1], out var step)
            && int.TryParse(parts[2], out var count)) {
            var chart = parts.Length >= 4 && Enum.TryParse<MuiChartKind>(parts[3], out var kind)
                ? kind : MuiChartKind.Bar;
            return new MuiPeriodSettings(interval, step, Math.Clamp(count, 1, MaxCount), chart);
        }

        return new MuiPeriodSettings();
    }

    // Private methods

    private DateTime Shift(DateTime now, int k)
        => Step switch {
            MuiStep.Week => now.AddDays(-7 * k),
            MuiStep.Month => now.AddMonths(-k),
            MuiStep.Quarter => now.AddMonths(-3 * k),
            _ => now - k * Length,
        };

    private string GetLabel(int k)
    {
        if (k == 0)
            return Interval == MuiInterval.Day ? "Last 24 hours" : "Last 7 days";

        return Step switch {
            MuiStep.Week => Plural(k, "week"),
            MuiStep.Month => Plural(k, "month"),
            MuiStep.Quarter => Plural(3 * k, "month"),
            _ => Plural(k, Interval == MuiInterval.Day ? "day" : "week"),
        } + " earlier";
    }

    private string GetShortLabel(int k)
        => k == 0 ? "now" : Step switch {
            MuiStep.Week => $"-{k}w",
            MuiStep.Month => $"-{k}mo",
            MuiStep.Quarter => $"-{3 * k}mo",
            _ => Interval == MuiInterval.Day ? $"-{k}d" : $"-{k}w",
        };

    private static string Plural(int count, string unit)
        => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}

public sealed record MuiWindow(int Index, DateTime From, DateTime To, string Label, string ShortLabel = "");
