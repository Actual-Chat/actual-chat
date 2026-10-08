using ActualChat.Time;
using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

public static class PeriodLocalizerExt
{
    extension(IStringLocalizer l)
    {
        // A period set as a count of days, hours or minutes, e.g. "2 days"
        public string PeriodText(TimeSpan period)
        {
            var (count, unit) = period.ToPeriodUnits();
            return unit switch {
                PeriodUnit.Days => l.Duration_DayCount(count, count),
                PeriodUnit.Hours => l.Duration_HourCount(count, count),
                _ => l.Duration_MinuteCount(count, count),
            };
        }

        // How long something spans: in minutes under an hour, in hours under two days, in days past that
        public string LengthText(TimeSpan length)
        {
            if (length.TotalHours < 1) {
                var minutes = (int)length.TotalMinutes;
                return l.Duration_MinuteCount(minutes, minutes);
            }
            if (length.TotalDays < 2) {
                var hours = (int)length.TotalHours;
                return l.Duration_HourCount(hours, hours);
            }

            var days = (int)length.TotalDays;
            return l.Duration_DayCount(days, days);
        }
    }
}
