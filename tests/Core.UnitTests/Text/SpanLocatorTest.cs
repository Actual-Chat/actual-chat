namespace ActualChat.Core.UnitTests.Text;

public class SpanLocatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("So, you know, I went, you know, home.", "you know", 1, 4, 12)]
    [InlineData("So, you know, I went, you know, home.", "you know", 2, 22, 30)]
    [InlineData("Like I like it.", "like", 2, 7, 11)]
    [InlineData("Ну вот, вот так.", "вот", 2, 8, 11)]
    [InlineData("Ну, э-э, я ждал.", "э-э", 1, 4, 7)]
    public void LocateShouldFindNthWholeWordOccurrence(string text, string word, int n, int start, int end)
    {
        // act
        var range = SpanLocator.Locate(text, word, n);

        // assert
        range.Should().Be(new Range<int>(start, end));
    }

    [Theory]
    [InlineData("I liked it.", "like", 1)]
    [InlineData("you know", "you know", 2)]
    [InlineData("", "um", 1)]
    [InlineData("um", "um", 0)]
    public void LocateShouldReturnNullWhenAbsent(string text, string word, int n)
        => SpanLocator.Locate(text, word, n).Should().BeNull("a partial match or a missing occurrence must not produce a span");

    [Fact]
    public void LocateShouldIgnoreCaseAndSurroundingPunctuation()
        => SpanLocator.Locate("Um... UM! (um)", "um", 3).Should().Be(new Range<int>(11, 13));

    [Fact]
    public void LocateShouldKeepApostrophesInsideWords()
        => SpanLocator.Locate("I don't know, don't.", "don't", 2).Should().Be(new Range<int>(14, 19));

    [Theory]
    [InlineData("あの私はあの店に行きました", "あの", 2, 4, 6)]
    [InlineData("えっと私は店に", "えっと", 1, 0, 3)]
    public void LocateShouldMatchSubstringsWhenWordsAreNotSpaceDelimited(string text, string word, int n, int start, int end)
        => SpanLocator.Locate(text, word, n, isWholeWord: false).Should().Be(new Range<int>(start, end));

    [Fact]
    public void LocateShouldStillRequireWholeWordsByDefault()
        => SpanLocator.Locate("あの私はあの店に", "あの", 2).Should().BeNull("kana runs have no word boundaries");
}
