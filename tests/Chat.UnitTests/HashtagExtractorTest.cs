namespace ActualChat.Chat.UnitTests;

public class HashtagExtractorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ShouldExtractTagsWithoutHash()
        => GetTags("see #promo and #promo-2_x now").Should().BeEquivalentTo(["promo", "promo-2_x"]);

    [Fact]
    public void ShouldLowercaseAndDeduplicateTags()
        => GetTags("#Promo #PROMO #promo").Should().BeEquivalentTo(["promo"]);

    [Fact]
    public void ShouldExtractTagsFromNestedMarkup()
        => GetTags(
            """
            # Heading with #tagInHeader
            **#tagInBold** and > #tagInQuote

            - #tagInListItem
            """)
            .Should()
            .BeEquivalentTo(["taginheader", "taginbold", "taginquote", "taginlistitem"]);

    [Fact]
    public void ShouldNotExtractTagsFromCode()
    {
        GetTags("`#promo`").Should().BeEmpty();
        GetTags(
            """
            ```
            #promo
            ```
            """)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ShouldNotExtractWhatIsNotATag()
    {
        // Same exclusions the renderer applies - see MarkupParserTest
        GetTags("c#5 item#2 #4121 #a#b # Header").Should().BeEmpty();
        GetTags("no tags here").Should().BeEmpty();
    }

    // Private methods

    private static HashSet<string> GetTags(string text)
        => HashtagExtractor.Instance.GetTags(new MarkupParser().Parse(text));
}
