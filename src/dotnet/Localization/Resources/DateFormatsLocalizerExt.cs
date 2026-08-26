using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

/// <summary>
/// Builds a <see cref="DateTimeFormatInfo"/> from the catalog's <c>Date_*</c> keys.
/// ICU is unavailable under InvariantGlobalization, so the catalog is the app's only
/// source of localized month names, day names and date patterns.
/// </summary>
public static class DateFormatsLocalizerExt
{
    private const char NameSeparator = '|';
    internal const int MonthCount = 12;
    internal const int DayCount = 7;

    public static DateTimeFormatInfo NewFormatInfo(this IStringLocalizer l)
    {
        var monthNames = ParseNames(l.Date_MonthNames, MonthCount);
        var monthGenitiveNames = ParseNames(l.Date_MonthGenitiveNames, MonthCount);
        var shortMonthNames = ParseNames(l.Date_ShortMonthNames, MonthCount);
        var dayNames = ParseNames(l.Date_DayNames, DayCount);
        var shortDayNames = ParseNames(l.Date_ShortDayNames, DayCount);

        // DateTimeFormatInfo wants a 13th, empty month name (the 13th month of lunar calendars)
        var result = (DateTimeFormatInfo)DateTimeFormatInfo.InvariantInfo.Clone();
        result.MonthNames = [..monthNames, ""];
        result.MonthGenitiveNames = [..monthGenitiveNames, ""];
        result.AbbreviatedMonthNames = [..shortMonthNames, ""];
        result.AbbreviatedMonthGenitiveNames = [..shortMonthNames, ""];
        result.DayNames = dayNames;
        result.AbbreviatedDayNames = shortDayNames;
        result.ShortestDayNames = shortDayNames;

        // Patterns go into the standard slots, so the UI formats with the standard
        // specifiers instead of hardcoding a field order that's only right for one
        // language: "t" time, "m" month-day, "d" short date, "D" full date, "y" year-month.
        result.ShortTimePattern = l.Date_TimePattern;
        result.MonthDayPattern = l.Date_DayMonthPattern;
        result.ShortDatePattern = l.Date_DayMonthYearPattern;
        result.LongDatePattern = l.Date_FullDatePattern;
        result.YearMonthPattern = l.Date_MonthYearPattern;
        return result;
    }

    // Private methods

    private static string[] ParseNames(string names, int expectedCount)
    {
        var result = names.Split(NameSeparator);
        return result.Length == expectedCount
            ? result
            : throw StandardError.Format($"Expected {expectedCount} names, but got {result.Length}: '{names}'.");
    }
}
