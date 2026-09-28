using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;

public static class CoachDayRange
{
    private const int MaxDays = 90;

    public static int DayCount(CoachWindow window)
        => window switch {
            CoachWindow.Today or CoachWindow.Week => 7,
            CoachWindow.Month => 30,
            _ => MaxDays,
        };

    // UTC days, like CoachDay.Day; the end is exclusive
    public static Range<Moment> For(CoachWindow window, Moment now)
    {
        var today = UsageDay.DayOf(now);
        return new Range<Moment>(today - TimeSpan.FromDays(DayCount(window) - 1), today + TimeSpan.FromDays(1));
    }
}
