namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTextStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static SpeechTextStats? Compute(string text)
        => SpeechTextStats.Compute(new PlayableTextMarkup(text, LinearMap.Zero));

    [Fact]
    public void ComputeShouldCountWordsSentencesAndQuestions()
    {
        // act
        var stats = Compute("I went home. Did you? Yes!\nGood.");

        // assert
        stats!.Words.Should().Be(7);
        stats.Sentences.Should().Be(4);
        stats.Questions.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldTreatLineBreakAsSentenceBoundary()
        => Compute("first line\nsecond line")!.Sentences.Should().Be(2);

    [Fact]
    public void ComputeShouldCountBackToBackRepetitionsAndMarkTheSecondWord()
    {
        // act
        var stats = Compute("So so I I think, think it's fine.");

        // assert
        stats!.Repetitions.Should().Be(3);
        stats.RepetitionSpans.Should().HaveCount(3);
        stats.RepetitionSpans[0].Should().Be(new SpeechSpan(SpeechSpanKind.Repetition, "so", 3, 2, ApiArray<string>.Empty));
        stats.RepetitionSpans[2].Word.Should().Be("think", "the comma between the two must not hide the repeat");
    }

    [Fact]
    public void ComputeShouldCountDistinctWordsCaseInsensitively()
        => Compute("The the THE cat cat.")!.DistinctWords.Should().Be(2);

    [Fact]
    public void ComputeShouldReturnNullForNoWords()
        => Compute("... !!!").Should().BeNull();

    [Fact]
    public void WordsShouldExcludePunctuationOnlyTokens()
        => Compute("a - b")!.Words.Should().Be(2);

    [Fact]
    public void WordsShouldKeepHyphenatedFillersWhole()
        => Compute("Ну, э-э, я ждал.")!.Words.Should().Be(4);

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("ru-RU", true)]
    [InlineData("ja-JP", false)]
    [InlineData("zh-CN", false)]
    [InlineData(null, true)]
    public void IsWordSplittableShouldRejectScriptsWithoutWordSpaces(string? tag, bool expected)
        => SpeechTextStats.IsWordSplittable(tag is null ? null : Language.Parse(tag)).Should().Be(expected);
}
