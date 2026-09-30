using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechChunkerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static string Sentence(int number, int words)
        => string.Join(" ", Enumerable.Range(0, words).Select(i => $"w{number}x{i}")) + ".";

    private static string Sentences(int count, int words)
        => string.Join(" ", Enumerable.Range(1, count).Select(n => Sentence(n, words)));

    private static int Words(string s)
        => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public void SplitShouldKeepAShortTextWhole()
    {
        // arrange
        var text = Sentences(3, 10);

        // act
        var chunks = SpeechChunker.Split(text, true, 40, 80, 40, 12);

        // assert
        chunks.Should().ContainSingle();
        chunks[0].Text.Should().Be(text);
        chunks[0].Context.Should().BeEmpty();
    }

    [Fact]
    public void SplitShouldCoverTheTextExactlyAndCutOnSentenceEnds()
    {
        // arrange
        var text = Sentences(12, 12);

        // act
        var chunks = SpeechChunker.Split(text, true, 30, 60, 40, 12);

        // assert
        chunks.Should().HaveCountGreaterThan(2);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text, "chunks must tile the text");
        chunks.Zip(chunks.Skip(1)).Should().OnlyContain(p => p.First.Start + p.First.Length == p.Second.Start);
        chunks.SkipLast(1).Should().OnlyContain(c => c.Text.TrimEnd().EndsWith("."));
        chunks.SkipLast(1).Should().OnlyContain(c => Words(c.Text) >= 30);
    }

    [Fact]
    public void SplitShouldGiveTheLastSentenceBeforeTheChunkAsContext()
    {
        // arrange
        var text = Sentences(12, 12);

        // act
        var chunks = SpeechChunker.Split(text, true, 30, 60, 40, 12);

        // assert
        chunks[0].Context.Should().BeEmpty();
        var previousLastSentence = chunks[0].Text.Trim().Split(". ").Last().TrimEnd('.') + ".";
        chunks[1].Context.Should().Be(previousLastSentence);
    }

    [Fact]
    public void SplitShouldLimitTheContextToTheLastWords()
    {
        // arrange
        var text = Sentence(1, 100) + " " + Sentences(6, 12);

        // act
        var chunks = SpeechChunker.Split(text, true, 30, 60, 10, 12);

        // assert
        chunks.Skip(1).Should().OnlyContain(c => Words(c.Context) <= 10);
        chunks.Skip(1).Should().OnlyContain(c => c.Context.Length > 0);
    }

    [Fact]
    public void SplitShouldCutALongSentenceWithoutPeriodsAtWordBoundaries()
    {
        // arrange
        var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"w{i}"));

        // act
        var chunks = SpeechChunker.Split(text, true, 40, 80, 20, 12);

        // assert
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => Words(c.Text) <= 80);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void SplitShouldNeverProduceMoreChunksThanAllowed()
    {
        // arrange
        var text = Sentences(100, 10);

        // act
        var chunks = SpeechChunker.Split(text, true, 40, 80, 40, 4);

        // assert
        chunks.Count.Should().BeLessThanOrEqualTo(4);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void SplitShouldCutScriptsWithoutSpacesAtTheirSentenceMarks()
    {
        // arrange
        var sentence = string.Concat(Enumerable.Repeat("これは長い文章です", 8)) + "。";
        var text = string.Concat(Enumerable.Repeat(sentence, 6));

        // act
        var chunks = SpeechChunker.Split(text, false, 30, 60, 40, 12);

        // assert
        chunks.Should().HaveCountGreaterThan(1);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
        chunks.SkipLast(1).Should().OnlyContain(c => c.Text.EndsWith("。"));
    }

    [Fact]
    public void SplitShouldReturnNothingForEmptyText()
        => SpeechChunker.Split("", true, 40, 80, 40, 12).Should().BeEmpty();
}
