namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void OnlyUzbekIsUnsupported()
    {
        // act
        var unsupported = Languages.All.Where(x => !SonioxLanguage.Supported.Contains(x)).ToArray();

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
    public void ToSonioxMapsCodes(string languageId, string expected)
        => Language.Parse(languageId).ToSoniox().Should().Be(expected);

    [Fact]
    public void SupportedLanguagesAllMapToNonEmptyCodes()
    {
        foreach (var language in SonioxLanguage.Supported)
            language.ToSoniox().Should().NotBeNullOrEmpty();
    }
}
