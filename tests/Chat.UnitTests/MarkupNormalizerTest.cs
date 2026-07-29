namespace ActualChat.Chat.UnitTests;

public class MarkupNormalizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("a\n\n\n\nb", "a\n\nb")]
    [InlineData("a\n\n\n\n\n\n\nb", "a\n\nb")]
    [InlineData("a\n\n\nb", "a\n\nb")]
    [InlineData("a\n\nb", "a\n\nb")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("\n\n\na", "a")]
    [InlineData("a\n\n\n", "a")]
    [InlineData("\n\n\na\n\n\n\nb\n\n\n", "a\n\nb")]
    [InlineData("# H\n\n\n\nb", "# H\n\nb")]
    [InlineData("a\n\n\n\n# H", "a\n\n# H")]
    public void BlankLineRunsAreCapped(string text, string expected)
    {
        // act
        var normalized = Normalize(text);

        // assert
        Escape(normalized).Should().Be(Escape(ToMarkupNewLines(expected)));
    }

    [Fact]
    public void CodeBlockContentIsNotTouched()
    {
        // act
        var normalized = Normalize("a\n\n\n\n```\nx\n\n\n\ny\n```\n\n\n\nb");

        // assert
        Escape(normalized).Should().Be(Escape(ToMarkupNewLines("a\n\n```\nx\n\n\n\ny\n```\n\nb")));
    }

    [Fact]
    public void UnchangedMarkupIsReturnedByReference()
    {
        // arrange
        var markup = new MarkupParser().Parse(ToMarkupNewLines("a\n\nb"));

        // act
        var normalized = MarkupNormalizer.Instance.Normalize(markup);

        // assert
        normalized.Should().BeSameAs(markup);
    }

    private static string Normalize(string text)
    {
        var markup = new MarkupParser().Parse(ToMarkupNewLines(text));
        return MarkupFormatter.Default.Format(MarkupNormalizer.Instance.Normalize(markup));
    }

    private static string ToMarkupNewLines(string text)
        => text.Replace("\n", NewLineMarkup.Instance.Text);

    private static string Escape(string text)
        => text.Replace("\r", "<CR>").Replace("\n", "<LF>");
}
