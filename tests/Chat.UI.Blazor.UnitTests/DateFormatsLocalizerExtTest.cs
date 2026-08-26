using System.Globalization;
using ActualChat.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class DateFormatsLocalizerExtTest
{
    private static readonly DateTime SampleDate = new(2026, 8, 14, 21, 5, 0, DateTimeKind.Unspecified);

    // Every shape the UI formats, as the standard specifier that reaches it
    private static readonly string[] Specifiers = ["t", "m", "d", "D", "y"];

    public static TheoryData<string> ShippedSubtags { get; } =
        new(StringCatalogs.ShippedSubtags(StringCatalogs.Kind.Strings));

    [Theory]
    [MemberData(nameof(ShippedSubtags))]
    public void EveryLanguageShouldBuildAFormatInfo(string subtag)
    {
        // arrange
        var l = NewLocalizer(subtag);

        // act
        var formats = DateFormatsLocalizerExt.NewFormatInfo(l);

        // assert
        formats.MonthNames.Should().HaveCount(DateFormatsLocalizerExt.MonthCount + 1);
        formats.AbbreviatedMonthNames.Should().HaveCount(DateFormatsLocalizerExt.MonthCount + 1);
        formats.DayNames.Should().HaveCount(DateFormatsLocalizerExt.DayCount);
        formats.AbbreviatedDayNames.Should().HaveCount(DateFormatsLocalizerExt.DayCount);
    }

    [Theory]
    [MemberData(nameof(ShippedSubtags))]
    public void EveryLanguageShouldNameEveryMonthAndDayDistinctly(string subtag)
    {
        // A duplicated name means a translation was pasted into the wrong slot -
        // and it renders as a plainly wrong date rather than as a missing string.

        // arrange
        var l = NewLocalizer(subtag);
        var formats = DateFormatsLocalizerExt.NewFormatInfo(l);

        // act
        var months = formats.MonthNames[..DateFormatsLocalizerExt.MonthCount];
        var days = formats.DayNames;

        // assert
        months.Should().OnlyHaveUniqueItems();
        formats.AbbreviatedMonthNames[..DateFormatsLocalizerExt.MonthCount].Should().OnlyHaveUniqueItems();
        days.Should().OnlyHaveUniqueItems();
        formats.AbbreviatedDayNames.Should().OnlyHaveUniqueItems();
        months.Should().NotContain(m => m.IsNullOrWhiteSpace());
        days.Should().NotContain(d => d.IsNullOrWhiteSpace());
    }

    [Theory]
    [MemberData(nameof(ShippedSubtags))]
    public void EveryLanguageShouldRenderEveryDatePattern(string subtag)
    {
        // The catalog patterns are reached through the standard specifiers, which is how
        // the UI formats - a translated-away "yyyy" or a stray literal shows up here as
        // a missing year or as an unchanged output across months.

        // arrange
        var l = NewLocalizer(subtag);
        var formats = DateFormatsLocalizerExt.NewFormatInfo(l);
        var other = SampleDate.AddMonths(3);

        // act
        var rendered = Specifiers.ToDictionary(x => x, x => SampleDate.ToString(x, formats));

        // assert
        rendered["t"].Should().Be("21:05");
        foreach (var (specifier, value) in rendered) {
            value.Should().NotBeNullOrWhiteSpace($"'{subtag}' must render \"{specifier}\"");
            value.Should().NotContain("$", $"'{subtag}' must not leak a format token into \"{specifier}\"");
        }
        var dayMonthTime = SampleDate.ToString(l.Date_DayMonthTimePattern, formats);
        var dayMonthYearTime = SampleDate.ToString(l.Date_DayMonthYearTimePattern, formats);
        dayMonthTime.Should().Contain("14").And.Contain("21:05").And.NotContain("2026");
        dayMonthYearTime.Should().Contain("14").And.Contain("21:05").And.Contain("2026");
        rendered["m"].Should().Contain("14", $"'{subtag}' month-day must show the day");
        rendered["d"].Should().Contain("2026", $"'{subtag}' short date must show the year");
        rendered["D"].Should().Contain("14").And.Contain("2026");
        foreach (var specifier in new[] { "m", "D", "y" })
            rendered[specifier].Should().NotBe(other.ToString(specifier, formats),
                $"'{subtag}' \"{specifier}\" must depend on the month");
    }

    [Fact]
    public void EnglishShouldRenderTheExpectedDates()
    {
        // arrange
        var l = NewLocalizer(Languages.English.IsoCode);
        var formats = DateFormatsLocalizerExt.NewFormatInfo(l);

        // act
        var rendered = new[] {
            SampleDate.ToString("t", formats),
            SampleDate.ToString("m", formats),
            SampleDate.ToString("d", formats),
            SampleDate.ToString("D", formats),
            SampleDate.ToString("y", formats),
            SampleDate.ToString("dddd", formats),
            SampleDate.ToString("ddd", formats),
            SampleDate.ToString(l.Date_DayMonthTimePattern, formats),
            SampleDate.ToString(l.Date_DayMonthYearTimePattern, formats),
        };

        // assert
        rendered.Should().Equal(
            "21:05", "Aug 14", "Aug 14, 2026", "August 14, 2026", "August 2026", "Friday", "Fri",
            "14 Aug, 21:05", "14 Aug 2026, 21:05");
    }

    [Fact]
    public void RussianShouldDeclineTheMonthWithADayNumber()
    {
        // The one case a plain name list can't cover: Slavic dates need the genitive
        // form next to a day number, and the nominative one on its own.

        // arrange
        var l = NewLocalizer(Languages.Russian.IsoCode);
        var formats = DateFormatsLocalizerExt.NewFormatInfo(l);

        // act
        var fullDate = SampleDate.ToString("D", formats);
        var monthYear = SampleDate.ToString("y", formats);

        // assert
        fullDate.Should().Be("14 августа 2026");
        monthYear.Should().Be("Август 2026");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Jan|Feb|Mar")]
    public void AMalformedNameListShouldFail(string monthNames)
    {
        // arrange
        var strings = StringCatalogs.Load(StringCatalogs.Kind.Strings, Languages.English)!;
        strings["Date_MonthNames"] = monthNames;

        // act
        var build = () => DateFormatsLocalizerExt.NewFormatInfo(new TestStringLocalizer(strings));

        // assert
        build.Should().Throw<FormatException>();
    }

    // Private methods

    private static TestStringLocalizer NewLocalizer(string subtag)
    {
        var language = Languages.AllUIAndTestOnly.Single(l => l.IsoCode == subtag);
        return new TestStringLocalizer(StringCatalogs.Load(StringCatalogs.Kind.Strings, language)!, language);
    }
}
