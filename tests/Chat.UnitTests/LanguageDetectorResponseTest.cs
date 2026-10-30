namespace ActualChat.Chat.UnitTests;

public sealed class LanguageDetectorResponseTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("""["en"]""", "en-US")]
    [InlineData("""["en", "ru"]""", "en-US", "ru-RU")]
    [InlineData("""```json ["ru"] ```""", "ru-RU")]
    [InlineData("""[]""")]
    public void ShouldParseDetectedLanguages(string response, params string[] expectedLanguageIds)
    {
        // arrange
        var expected = expectedLanguageIds.Select(Language.Parse).ToArray();

        // act
        var languages = LanguageDetector.ParseResponse(response);

        // assert
        languages.Should().Equal(expected);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("ka")]
    [InlineData("xx")]
    public void UndeclaredCodeShouldNotDropTheOthers(string code)
    {
        // act
        var languages = LanguageDetector.ParseResponse($"""["en", "{code}", "ru"]""");

        // assert
        languages.Should().Equal([Languages.English, Languages.Russian],
            "one code the build doesn't declare must not cost the message its other languages");
    }

    [Fact]
    public void PreviewLanguageShouldBeSkipped()
    {
        // act
        var languages = LanguageDetector.ParseResponse("""["kn", "en"]""");

        // assert
        languages.Should().Equal([Languages.English], "a Preview language is held back as a whole");
    }
}
