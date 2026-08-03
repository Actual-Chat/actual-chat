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
}
