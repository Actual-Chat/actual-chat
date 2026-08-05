using ActualChat.Streaming;

namespace ActualChat.Transcription.UnitTests;

public sealed class CountWordsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Hello", 1)]
    [InlineData("Hello, how are you?", 4)]
    [InlineData("  spaced   out  text  ", 3)]
    [InlineData("Привет, как дела", 3)]
    [InlineData("It's a well-known fact", 4)]
    [InlineData("안녕하세요 반갑습니다", 2)]
    public void SpacedScriptsCountWordRuns(string text, int expected)
        => text.CountWords().Should().Be(expected);

    [Theory]
    [InlineData("你好世界", 2)]
    [InlineData("日本語のテスト", 4)]
    [InlineData("สวัสดีครับ", 2)]
    public void UnspacedScriptsCountCharactersInstead(string text, int expected)
        => text.CountWords().Should().Be(expected);

    [Fact]
    public void PunctuationIsNotAWord()
        => "... --- ?!".CountWords().Should().Be(0);
}
