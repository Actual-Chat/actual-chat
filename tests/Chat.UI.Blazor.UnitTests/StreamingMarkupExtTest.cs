using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class StreamingMarkupExtTest
{
    [Theory]
    [InlineData("")]
    [InlineData("Just transcribed words")]
    [InlineData("A sentence that keeps going")]
    public void PlainTextKeepsTheCharacterAnimation(string text)
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText(text);

        // assert
        markup.Should().BeNull();
    }

    [Theory]
    [InlineData("Hello **bold")]
    [InlineData("Hello **bold**")]
    [InlineData("Check `code")]
    [InlineData("- one\n- two")]
    public void MarkupIsRenderedAsMarkup(string text)
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText(text);

        // assert
        markup.Should().NotBeNull();
    }

    [Fact]
    public void UnterminatedSpoilerIsMaskedWhileStreaming()
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText("Ending: ||the butler did i");

        // assert
        markup.Should().NotBeNull();
        markup!.ToReadableText().Should().NotContain("butler");
        markup.ToReadableText().Should().Contain(StylizedMarkup.SpoilerMaskChar.ToString());
    }

    [Fact]
    public void UnterminatedSpoilerWithHalfArrivedCloserIsAlsoMasked()
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText("Ending: ||the butler|");

        // assert
        markup.Should().NotBeNull();
        markup!.ToReadableText().Should().NotContain("butler");
    }

    [Fact]
    public void UnterminatedBoldRendersAsBoldRatherThanRawTokens()
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText("Hello **wor");

        // assert
        var seq = markup.Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<MarkupSeq>().Subject;
        var bold = seq.Items[^1].Should().BeOfType<StylizedMarkup>().Subject;
        bold.Style.Should().Be(TextStyle.Bold);
        bold.IsIncomplete.Should().BeTrue();
        bold.Content.ToReadableText().Should().Be("wor");
    }

    [Theory]
    [InlineData("see [Guide](https://exa", "see Guide")]
    [InlineData("see [Guide](", "see Guide")]
    [InlineData("see [Guide](https://x.y/f_(b)", "see Guide")]
    [InlineData("\n\nsee [Guide](https://exa", "see Guide")]
    public void HalfArrivedLinkShouldNotShowItsSyntaxInTheRawView(string text, string expected)
    {
        // The parse turns the half-arrived link into plain text, which must not send the view back to raw
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText(text);

        // assert
        markup.Should().NotBeNull();
        markup!.ToReadableText().Should().Be(expected);
    }

    [Theory]
    [InlineData("a\r\nb\r\n\r\nc  ")]
    [InlineData("a\nb\n\nc  ")]
    [InlineData("  leading and trailing  ")]
    [InlineData("\n\nhello")]
    [InlineData("\n\n  hello\n\n")]
    [InlineData("\r\n\r\nhello")]
    public void PlainTextWithOddWhitespaceShouldKeepTheRawView(string text)
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText(text);

        // assert
        markup.Should().BeNull();
    }

    [Fact]
    public void ResetFollowedByPlainTextShouldNotFallBackToTheRawView()
    {
        // The raw view would show the dropped draft and the marker itself
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText("Draft one\n<!--reset-->\nFinal answer");

        // assert
        markup.Should().NotBeNull();
        markup!.ToReadableText().Should().Be("Final answer");
    }

    [Fact]
    public void HalfArrivedResetShouldNotFallBackToTheRawView()
    {
        // act
        var markup = StreamingMarkupExt.ParseOrNullIfPlainText("old\n<!--re");

        // assert
        markup.Should().NotBeNull();
        markup!.ToReadableText().Should().Be("old");
    }

    [Theory]
    [InlineData("old\n<!--reset-->\nnew", "new")]
    [InlineData("old\r\n<!--reset-->\r\n\r\nnew", "new")]
    [InlineData("a\n<!--reset-->\nb\n<!--reset-->\nc", "c")]
    [InlineData("old\n<!--re", "old")]
    [InlineData("no reset here", "no reset here")]
    public void DropResetsShouldGiveWhatAReaderSees(string text, string expected)
    {
        // act
        var visible = MarkupParser.DropResets(text);

        // assert
        visible.Replace("\r\n", "\n").Should().Be(expected);
    }

    [Theory]
    [InlineData("```\n<!--reset-->\n```")]
    [InlineData("```\ncode\n<!--re")]
    public void MarkerInsideACodeBlockShouldBeLeftAlone(string text)
    {
        // act
        var visible = MarkupParser.DropResets(text);

        // assert
        visible.Replace("\r\n", "\n").Should().Be(text);
    }
}
