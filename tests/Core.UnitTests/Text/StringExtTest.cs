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
}
