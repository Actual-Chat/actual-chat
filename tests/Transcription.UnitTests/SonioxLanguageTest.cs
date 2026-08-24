namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void OnlyUzbekAndMontenegrinShouldBeUnsupported()
    {
        // act
        var unsupported = Languages.All.Where(x => !SonioxLanguage.Supported.Contains(x)).ToArray();

        // assert
        WriteLine(unsupported.Select(x => x.Value).ToDelimitedString());
        unsupported.Should().BeEquivalentTo([Languages.Uzbek, Languages.Montenegrin]);
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
