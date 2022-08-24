namespace ActualChat.Core.UnitTests.Search;

public class SearchPhraseTest : TestBase
{
    public SearchPhraseTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void BasicTest()
    {
        var sp = "dodd".ToSearchPhrase(true, false);
        Out.WriteLine(sp.ToString());
        Out.WriteLine(sp.GetMatch("Admiral Dodd Rancit").ToString());

        "".ToSearchPhrase(false, false).GetTermRegexString().Should().Be("");
        "".ToSearchPhrase(true, true).GetTermRegexString().Should().Be("");

        "abc".ToSearchPhrase(false, false).GetTermRegexString()
            .Should().Be("((^|\\s)?abc)");
        "abc".ToSearchPhrase(false, true).GetTermRegexString()
            .Should().Be("((^|\\s)?abc)|((^|\\s)?bc)|((^|\\s)?c)");
        "abc".ToSearchPhrase(true, false).GetTermRegexString()
            .Should().Be("((^|\\s)?a(b(c)?)?)");
        "abc".ToSearchPhrase(true, true).GetTermRegexString()
            .Should().Be("((^|\\s)?a(b(c)?)?)|((^|\\s)?b(c)?)|((^|\\s)?c)");

        "abc de".ToSearchPhrase(false, false).GetTermRegexString()
            .Should().Be("((^|\\s)?abc)|((^|\\s)?de)");
        "abc de".ToSearchPhrase(false, true).GetTermRegexString()
            .Should().Be("((^|\\s)?abc)|((^|\\s)?bc)|((^|\\s)?c)|((^|\\s)?de)|((^|\\s)?e)");
        "abc de".ToSearchPhrase(true, false).GetTermRegexString()
            .Should().Be("((^|\\s)?a(b(c)?)?)|((^|\\s)?d(e)?)");
        "abc de".ToSearchPhrase(true, true).GetTermRegexString()
            .Should().Be("((^|\\s)?a(b(c)?)?)|((^|\\s)?b(c)?)|((^|\\s)?c)|((^|\\s)?d(e)?)|((^|\\s)?e)");

        "abc_de".ToSearchPhrase(false, false).GetTermRegexString()
            .Should().Be("((^|\\s)?abc)|((^|\\s)?de)");
    }
}
