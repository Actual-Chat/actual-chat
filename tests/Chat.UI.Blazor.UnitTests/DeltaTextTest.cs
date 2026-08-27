using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class DeltaTextTest
{
    private static readonly DateTime Now = new(2026, 8, 14, 21, 5, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(-3, "just now")]
    [InlineData(3, "just now")]
    [InlineData(-8, "just now")]
    [InlineData(-30, "few seconds ago")]
    [InlineData(30, "in few seconds")]
    [InlineData(-90, "a minute ago")]
    [InlineData(90, "in about 1 minute")]
    [InlineData(-3 * 60, "few minutes ago")]
    [InlineData(3 * 60, "in few minutes")]
    [InlineData(-7 * 60, "7 min ago")]
    [InlineData(7 * 60, "in 7 min")]
    [InlineData(-3 * 3600, "18:05")]
    [InlineData(-24 * 3600, "yesterday at 21:05")]
    [InlineData(24 * 3600, "tomorrow at 21:05")]
    [InlineData(-5 * 24 * 3600, "Sun at 21:05")]
    [InlineData(-30 * 24 * 3600, "Jul 15 at 21:05")]
    [InlineData(-400 * 24 * 3600, "Jul 10, 2025 at 21:05")]
    public void EnglishShouldRenderTheExpectedDelta(int offsetSeconds, string expected)
    {
        // arrange
        using var context = NewContext(Languages.English);
        var deltaText = context.Services.GetRequiredService<DeltaText>();

        // act
        var (text, _) = deltaText.Get(Now.AddSeconds(offsetSeconds), Now);

        // assert
        text.Should().Be(expected);
    }

    [Fact]
    public void RussianShouldRenderTheExpectedDelta()
    {
        // arrange
        using var context = NewContext(Languages.Russian);
        var deltaText = context.Services.GetRequiredService<DeltaText>();

        // act
        var rendered = new[] {
            deltaText.Get(Now.AddSeconds(-3), Now).Text,
            deltaText.Get(Now.AddMinutes(-7), Now).Text,
            deltaText.Get(Now.AddDays(-1), Now).Text,
            deltaText.Get(Now.AddDays(-5), Now).Text,
        };

        // assert
        rendered.Should().Equal("только что", "7 мин. назад", "вчера в 21:05", "вс в 21:05");
    }

    [Theory]
    [MemberData(nameof(DateFormatsLocalizerExtTest.ShippedSubtags), MemberType = typeof(DateFormatsLocalizerExtTest))]
    public void EveryLanguageShouldRenderEveryDelta(string subtag)
    {
        // arrange
        var language = Languages.AllUIAndTestOnly.Single(l => l.IsoCode == subtag);
        using var context = NewContext(language);
        var deltaText = context.Services.GetRequiredService<DeltaText>();
        var offsets = new[] { -3, -30, 30, -90, 90, -180, 180, -420, 420, -10800, -86400, 86400, -432000 };

        // act
        var rendered = offsets.Select(o => deltaText.Get(Now.AddSeconds(o), Now).Text).ToList();

        // assert
        rendered.Should().OnlyContain(t => !t.IsNullOrWhiteSpace(), $"'{subtag}' must render every delta");
        rendered.Should().NotContain(t => t.Contains('{'), $"'{subtag}' must not leak a placeholder");
    }

    // Private methods

    private static BunitContext NewContext(Language language)
    {
        var context = TestBunitContext.New(StringCatalogs.Load(StringCatalogs.Kind.Strings, language), language);
        context.Services
            .AddScoped<DateFormatter>()
            .AddScoped<DeltaText>();
        return context;
    }
}
