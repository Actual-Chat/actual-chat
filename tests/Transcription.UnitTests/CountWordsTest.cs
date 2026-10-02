using System.Numerics;

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

    [Theory]
    [InlineData(-1f, 6)]
    [InlineData(0f, 6)]
    [InlineData(2f, 3)]
    [InlineData(2.8f, 1)]
    [InlineData(3f, 0)]
    public void CountWordsSinceShouldSkipWordsSaidEarlier(float time, int expected)
    {
        // arrange - "one two three " takes the first two seconds, "four five six" the third
        var transcript = new Transcript(
            "one two three four five six",
            new LinearMap(new Vector2(0, 0), new Vector2(14, 2), new Vector2(27, 3)),
            []);

        // act
        var count = transcript.CountWordsSince(time);

        // assert
        count.Should().Be(expected);
    }
}
