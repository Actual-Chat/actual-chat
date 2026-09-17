namespace ActualChat.Transcription.UnitTests;

public class ClauseSplitterTest
{
    [Fact]
    public void SentenceEndsAreClauseEnds()
        => ClauseSplitter.Split("Hello world. How are you? Fine!", 0, false)
            .Should().Equal([12, 25, 31]);

    [Fact]
    public void ACommaClosesAClauseOnlyPastTheMinimumLength()
    {
        var reason = "\"Well,\" is too short to be spoken alone, so the comma after \"there\" closes the first clause";
        ClauseSplitter.Split("Well, I think we should go there, and then", 0, false)
            .Should().Equal([33], reason);
        ClauseSplitter.Split("Yes, no", 0, false).Should().BeEmpty();
    }

    [Fact]
    public void PunctuationInsideAWordOrNumberIsNoBoundary()
        => ClauseSplitter.Split("It costs 3.5 dollars at 10:30 today. Ok", 0, false)
            .Should().Equal([36]);

    [Fact]
    public void FullwidthMarksNeedNoFollowingSpace()
        => ClauseSplitter.Split("你好。你好吗？", 0, false).Should().Equal([3, 7]);

    [Fact]
    public void SplittingStartsAtFrom()
        => ClauseSplitter.Split("Hello world. How are you?", 12, false).Should().Equal([25]);

    [Fact]
    public void AnUnterminatedRemainderIsAClauseOnlyAtTheEnd()
    {
        ClauseSplitter.Split("Hello world. How are", 0, false).Should().Equal([12]);
        ClauseSplitter.Split("Hello world. How are", 0, true).Should().Equal([12, 20]);
        ClauseSplitter.Split("", 0, true).Should().BeEmpty();
        ClauseSplitter.Split("Hello world. ", 0, true).Should().Equal([12], "a whitespace-only remainder is no clause");
    }

    [Fact]
    public void ALongUnpunctuatedRunIsCutAtTheLastSpaceBeforeTheLimit()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 40)); // 199 chars, no punctuation
        var ends = ClauseSplitter.Split(words, 0, false);
        ends.Should().HaveCount(1);
        ends[0].Should().BeLessThanOrEqualTo(ClauseSplitter.MaxUnpunctuatedLength);
        words[ends[0] - 1].Should().NotBe(' ', "the cut sits after a word");
        words[ends[0]].Should().Be(' ', "and before the space, so no word is split");
    }

    [Fact]
    public void ALongRunWithoutSpacesIsCutAtTheLimit()
    {
        var run = new string('x', ClauseSplitter.MaxUnpunctuatedLength + 5);
        ClauseSplitter.Split(run, 0, false).Should().Equal([ClauseSplitter.MaxUnpunctuatedLength]);
    }

    [Fact]
    public void ALongUnpunctuatedRunBeforeABoundaryIsCappedToo()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 40)); // 199 chars, no punctuation
        var input = words + ". End";
        var ends = ClauseSplitter.Split(input, 0, false);
        ends.Should().HaveCount(2);
        ends[0].Should().BeLessThanOrEqualTo(ClauseSplitter.MaxUnpunctuatedLength);
        words[ends[0] - 1].Should().NotBe(' ', "the cut sits after a word");
        words[ends[0]].Should().Be(' ', "and before the space, so no word is split");
        ends[1].Should().Be(input.IndexOf('.') + 1, "the dot boundary comes after the overflow cut");
        for (var i = 0; i < ends.Count; i++) {
            var clauseStart = i == 0 ? 0 : ends[i - 1];
            var clauseEnd = ends[i];
            var clauseLength = clauseEnd - clauseStart;
            clauseLength.Should().BeLessThanOrEqualTo(ClauseSplitter.MaxUnpunctuatedLength,
                $"clause {i} from {clauseStart} to {clauseEnd} must be ≤ 120");
        }
    }

    [Fact]
    public void ALongRunWithoutSpacesBeforeABoundaryIsCutAtTheLimit()
    {
        var input = new string('x', ClauseSplitter.MaxUnpunctuatedLength + 10) + ". End";
        var dotIndex = input.IndexOf('.');
        ClauseSplitter.Split(input, 0, false).Should().Equal([ClauseSplitter.MaxUnpunctuatedLength,
            dotIndex + 1]);
    }
}
