namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void OnlyUzbekShouldBeUnsupported()
    {
        // act
        var unsupported = Languages.AllTranscription
            .Where(x => !SonioxLanguage.Supported.Contains(x))
            .ToArray();

        // assert
        WriteLine(unsupported.Select(x => x.Value).ToDelimitedString());
        unsupported.Should().BeEquivalentTo([Languages.Uzbek]);
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("zh-TW", "zh")]
    // Soniox exposes Tagalog only, and "fil" would be rejected.
    [InlineData("fil-PH", "tl")]
    // Soniox knows no "cnr"; Montenegrin goes as Serbian.
    [InlineData("cnr-ME", "sr")]
    [InlineData("no-NO", "no")]
    public void ToSonioxShouldMapCodes(string languageId, string expected)
        => Language.Parse(languageId).ToSoniox().Should().Be(expected);

    [Theory]
    [InlineData("sr", "cnr-ME", "cnr-ME")]
    [InlineData("sr", "sr-SR,cnr-ME", "cnr-ME")]
    [InlineData("sr", "", "sr-SR")]
    [InlineData("sr", "en-US", "sr-SR")]
    [InlineData("tl", "fil-PH", "fil-PH")]
    [InlineData("ru", "cnr-ME", "ru-RU")]
    // Only the override languages are told apart: "zh" stays Chinese for a zh-TW speaker.
    [InlineData("zh", "zh-TW", "zh-CN")]
    public void FromSonioxShouldPreferAnExpectedOverrideLanguage(string code, string expectedLanguages, string expected)
    {
        // arrange
        var languages = expectedLanguages
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Language.Parse)
            .ToArray();

        // act
        var language = SonioxLanguage.FromSoniox(code, languages);

        // assert
        language.Should().Be(Language.Parse(expected));
    }

    [Fact]
    public void FromSonioxShouldDropPreviewLanguages()
    {
        // act, assert
        foreach (var language in Languages.All.Where(x => x.Support.HasFlag(LanguageSupport.Preview)))
            SonioxLanguage.FromSoniox(language.ToSoniox(), [language]).Should().BeNull($"{language} isn't released");
    }

    [Fact]
    public void EverySupportedLanguageShouldMapToNonEmptyCode()
    {
        // act
        var codes = SonioxLanguage.Supported.Select(x => x.ToSoniox()).ToArray();

        // assert
        codes.Should().OnlyContain(x => !x.IsNullOrEmpty());
    }
}
