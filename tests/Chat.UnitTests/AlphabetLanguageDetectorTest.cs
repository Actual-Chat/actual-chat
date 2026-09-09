namespace ActualChat.Chat.UnitTests;

public class AlphabetLanguageDetectorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("hello world", "en-US")]
    [InlineData("Hello, Bob!", "en-US")]
    [InlineData("привет мир", "ru-RU")]
    [InlineData("Доброе утро", "ru-RU")]
    [InlineData("こんにちは", "ja-JP")]
    [InlineData("안녕하세요", "ko-KR")]
    [InlineData("你好世界", "zh-CN")]
    [InlineData("नमस्ते", "hi-IN")]
    [InlineData("สวัสดี", "th-TH")]
    [InlineData("café résumé naïve", "en-US")] // Latin extended chars still classify as English
    public void ShouldDetectSingleAlphabet(string text, string expectedLanguageId)
    {
        // act
        var languages = AlphabetLanguageDetector.Detect(text);

        // assert
        languages.Should().Equal(Language.Parse(expectedLanguageId));
    }

    [Theory]
    [InlineData("Привет", "Hello", "ru-RU", true)]
    [InlineData("Привет", "Привет!", "ru-RU", false)]
    [InlineData("Hello", "Привет, **Bob**!", "ru-RU", false)]
    [InlineData("Hello", "Привет", "en-US", true)]
    [InlineData("Hola", "Hello", "en-US", false)]
    [InlineData("Hello", "こんにちは", "ja-JP", false)]
    [InlineData("Hello", "Hello", "ja-JP", false)]
    [InlineData("123", "Hello", "ru-RU", false)]
    [InlineData("Привет", "", "ru-RU", false)]
    public void ShouldDetectScriptMismatch(string source, string translation, string targetId, bool expected)
    {
        // act
        var isMismatch = AlphabetLanguageDetector.IsScriptMismatch(source, translation, Language.Parse(targetId));

        // assert
        isMismatch.Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello, Привет!", "en-US", "ru-RU")]
    [InlineData("привет hello", "en-US", "ru-RU")]
    [InlineData("Hello こんにちは 你好", "en-US", "ja-JP", "zh-CN")]
    public void ShouldDetectMultipleAlphabets(string text, params string[] expectedLanguageIds)
    {
        // arrange
        var expected = expectedLanguageIds.Select(Language.Parse).ToArray();

        // act
        var languages = AlphabetLanguageDetector.Detect(text);

        // assert
        languages.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("!@#$%^&*()")]
    [InlineData("0xDEADBEEF")] // hex digits classify as English via the Latin a..f letters
    public void ShouldReturnEmptyForNonLetterOrEmpty(string text)
    {
        // act
        var languages = AlphabetLanguageDetector.Detect(text);

        // assert
        if (text == "0xDEADBEEF")
            languages.Should().Equal(Languages.English);
        else
            languages.Should().BeEmpty();
    }

    [Fact]
    public void ShouldHandleNullOrWhitespace()
    {
        AlphabetLanguageDetector.Detect("").Should().BeEmpty();
        AlphabetLanguageDetector.Detect("   \t\n").Should().BeEmpty();
    }
}
