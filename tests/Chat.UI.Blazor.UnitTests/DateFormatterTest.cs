using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class DateFormatterTest
{
    private static readonly DateTime Now = new(2026, 8, 14, 21, 5, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Yesterday")]
    [InlineData(3, "Tue")]
    [InlineData(30, "Jul 15")]
    [InlineData(400, "Jul 10, 2025")]
    public void EnglishShouldRenderTheRelativeDate(int daysAgo, string expected)
    {
        // arrange
        var formatter = NewFormatter(Languages.English);

        // act
        var date = formatter.FormatRelativeDate(Now.AddDays(-daysAgo), Now);

        // assert
        date.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "1 min")]
    [InlineData(30, "1 min")]
    [InlineData(45 * 60, "45 min")]
    [InlineData(2 * 3600 + 5 * 60, "2 h 5 min")]
    [InlineData(26 * 3600, "1 d 2 h 0 min")]
    public void EnglishShouldRenderTheDuration(int seconds, string expected)
    {
        // arrange
        var formatter = NewFormatter(Languages.English);

        // act
        var duration = formatter.FormatDuration(TimeSpan.FromSeconds(seconds));

        // assert
        duration.Should().Be(expected);
    }

    [Fact]
    public void ANegativeDurationShouldNotUnderflow()
    {
        // arrange
        var formatter = NewFormatter(Languages.English);

        // act
        var duration = formatter.FormatDuration(TimeSpan.FromSeconds(-10));

        // assert
        duration.Should().Be("1 min");
    }

    [Theory]
    [MemberData(nameof(DateFormatsLocalizerExtTest.ShippedSubtags), MemberType = typeof(DateFormatsLocalizerExtTest))]
    public void EveryLanguageShouldRenderRelativeDatesAndDurations(string subtag)
    {
        // arrange
        var language = Languages.AllUIAndTestOnly.Single(l => l.IsoCode == subtag);
        var formatter = NewFormatter(language);

        // act
        var rendered = new[] { 0, 1, 3, 30, 400 }
            .Select(d => formatter.FormatRelativeDate(Now.AddDays(-d), Now))
            .Append(formatter.FormatDuration(TimeSpan.FromHours(26)))
            .ToList();

        // assert
        rendered.Should().OnlyHaveUniqueItems($"'{subtag}' must distinguish every relative date");
        rendered.Should().NotContain(t => t.IsNullOrWhiteSpace() || t.Contains('{'),
            $"'{subtag}' must render without empty values or leaked placeholders");
    }

    [Fact]
    public void ALanguageChangeShouldBePickedUpWithoutANewScope()
    {
        // The format info is cached per language, not per instance, so it follows a language
        // change on the same lookup the strings do - rather than pinning whatever language the
        // scope was created in and relying on the app reloading.

        // arrange
        var localizer = NewLocalizer(Languages.English);
        var formatter = NewFormatter(localizer);
        var before = Now.ToString("D", formatter);

        // act
        localizer.SwitchTo(Languages.Russian, Catalog(Languages.Russian));
        var after = Now.ToString("D", formatter);

        // assert
        before.Should().Be("August 14, 2026");
        after.Should().Be("14 августа 2026");
    }

    // Private methods

    private static DateFormatter NewFormatter(Language language)
        => NewFormatter(NewLocalizer(language));

    private static DateFormatter NewFormatter(IStringLocalizer localizer)
        => new ServiceCollection()
            .AddSingleton(localizer)
            .AddScoped<DateFormatter>()
            .BuildServiceProvider()
            .GetRequiredService<DateFormatter>();

    private static TestStringLocalizer NewLocalizer(Language language)
        => new(Catalog(language), language);

    private static Dictionary<string, string> Catalog(Language language)
        => StringCatalogs.Load(StringCatalogs.Kind.Strings, language)!;
}
