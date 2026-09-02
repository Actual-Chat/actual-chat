namespace ActualChat.Core.UnitTests.Text;

public class StringExtTest
{
    [Fact]
    public void ToSentenceCaseTest()
    {
        "BlaZor".ToSentenceCase().Should().Be("Bla Zor");
        "blaZor".ToSentenceCase().Should().Be("bla Zor");
        "bla12".ToSentenceCase().Should().Be("bla 12");
        "BLA13".ToSentenceCase().Should().Be("BLA 13");
        "bla12Zor".ToSentenceCase().Should().Be("bla 12 Zor");
        "BLA13Zor".ToSentenceCase().Should().Be("BLA 13 Zor");
        "someUI".ToSentenceCase().Should().Be("some UI");
        "someUI1".ToSentenceCase().Should().Be("some UI 1");
        "1st".ToSentenceCase().Should().Be("1st");
        "1X".ToSentenceCase().Should().Be("1 X");
        "xUI".ToSentenceCase().Should().Be("x UI");
    }

    [Theory]
    [InlineData("", "  ", "")]
    [InlineData("hello", "", "hello")]
    [InlineData("hello", "  ", "  hello")]
    [InlineData("line1\nline2", "  ", "  line1\n  line2")]
    [InlineData("line1\nline2\nline3", "  ", "  line1\n  line2\n  line3")]
    [InlineData("line1\nline2\n", "  ", "  line1\n  line2\n")]
    [InlineData("a\nb\nc\n", "\t", "\ta\n\tb\n\tc\n")]
    [InlineData("\n", "  ", "  \n")]
    [InlineData("\n\n", "  ", "  \n  \n")]
    [InlineData("single", ">>", ">>single")]
    [InlineData("a\r\nb", "  ", "  a\r\n  b")]
    public void IndentTest(string source, string indent, string expected)
        => source.Indent(indent).Should().Be(expected);

    [Fact]
    public void ParseLinesShouldKeepEveryLine()
    {
        // act
        var lines = "first\nsecond\r\n\nlast".ParseLines().ToArray();

        // assert
        lines.Should().Equal(("first", true), ("second", true), ("", true), ("last", false));
    }
}
