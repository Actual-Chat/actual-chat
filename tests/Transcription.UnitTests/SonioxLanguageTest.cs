namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void OnlyUzbekShouldBeUnsupported()
    {
        // Montenegrin isn't listed here: it never enters AllTranscription, because
        // LanguageSupport.UI without Transcription keeps it out of the spoken-language set.

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
    public void ToSonioxShouldMapCodes(string languageId, string expected)
        => Language.Parse(languageId).ToSoniox().Should().Be(expected);

    [Fact]
    public void EverySupportedLanguageShouldMapToNonEmptyCode()
    {
        // act
        var codes = SonioxLanguage.Supported.Select(x => x.ToSoniox()).ToArray();

        // assert
        codes.Should().OnlyContain(x => !string.IsNullOrEmpty(x));
    }
}
