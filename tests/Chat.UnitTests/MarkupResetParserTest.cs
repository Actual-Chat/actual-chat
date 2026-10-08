namespace ActualChat.Chat.UnitTests;

public class MarkupResetParserTest
{
    private const string Reset = "<!--reset-->";

    private static readonly IMarkupParser Parser = new MarkupParser();
    private static readonly IMarkupParser IncompleteParser = new MarkupParser { AllowIncompleteMarkup = true };
    private static readonly IMarkupParser UnparsedParser = new MarkupParser { UseUnparsedTextMarkup = true };

    [Fact]
    public void ResetShouldDropEverythingAboveIt()
    {
        // act
        var markup = Parser.Parse($"old text\n{Reset}\nnew text");

        // assert
        Text(markup).Should().Be("new text");
    }

    [Fact]
    public void ResetShouldDropBlocksOfAnyKindAboveIt()
    {
        // arrange
        var text = $"# Title\n\n- a\n- b\n\n> quote\n\n| x |\n| --- |\n| 1 |\n\n---\n{Reset}\nafter";

        // act
        var markup = Parser.Parse(text);

        // assert
        Text(markup).Should().Be("after");
    }

    [Fact]
    public void LastResetShouldWin()
    {
        // act
        var markup = Parser.Parse($"one\n{Reset}\ntwo\n{Reset}\nthree");

        // assert
        Text(markup).Should().Be("three");
    }

    [Fact]
    public void LastOfThreeResetsShouldWin()
    {
        // act
        var markup = Parser.Parse($"one\n{Reset}\ntwo\n{Reset}\nthree\n{Reset}\nfour");

        // assert
        Text(markup).Should().Be("four");
    }

    [Fact]
    public void AdjacentResetsShouldActAsOne()
    {
        // act
        var markup = Parser.Parse($"one\n{Reset}\n{Reset}\n\n{Reset}\nafter");

        // assert
        Text(markup).Should().Be("after");
    }

    [Fact]
    public void ResetsAroundACodeBlockWithAMarkerShouldDropTheBlockToo()
    {
        // The marker inside the fence is code, but the real one after the block still wins
        // act
        var markup = Parser.Parse($"one\n{Reset}\n```\n{Reset}\n```\n{Reset}\nlast");

        // assert
        Text(markup).Should().Be("last");
    }

    [Fact]
    public void FencedMarkerBetweenTwoResetsShouldNotBeCountedAsOne()
    {
        // act
        var markup = Parser.Parse($"{Reset}\nkept\n```\n{Reset}\n```\nalso kept");

        // assert
        Text(markup).Should().Contain("kept").And.Contain("also kept");
        markup.Format().Should().Contain(Reset, "the fenced marker is code");
    }

    [Fact]
    public void ManyResetsShouldKeepOnlyWhatFollowsTheLast()
    {
        // arrange
        var text = string.Concat(Enumerable.Range(0, 20).Select(i => $"part {i}\n{Reset}\n")) + "final";

        // act
        var markup = Parser.Parse(text);

        // assert
        Text(markup).Should().Be("final");
    }

    [Fact]
    public void StreamedTextWithSeveralResetsShouldShowOnlyTheCurrentSegmentAtEveryPrefix()
    {
        // arrange
        var segments = new[] { "alpha one", "beta two", "gamma three" };
        var text = string.Join($"\n{Reset}\n", segments);

        // act & assert
        for (var length = 1; length <= text.Length; length++) {
            var prefix = text[..length];
            var readable = Text(IncompleteParser.Parse(prefix));
            readable.Should().NotContain("<!--", $"after {length} chars");
            var lastLine = prefix[(prefix.LastIndexOf('\n') + 1)..];
            var isMarkerHalfArrived = lastLine.Length > 0 && Reset.StartsWith(lastLine);
            var lastMarkerEnd = prefix.LastIndexOf(Reset + "\n");
            if (lastMarkerEnd >= 0 && !isMarkerHalfArrived)
                readable.Should().Be(prefix[(lastMarkerEnd + Reset.Length + 1)..].Trim(), $"after {length} chars");
        }
    }

    [Theory]
    [InlineData("``` ```\n{0}\nnew")]
    [InlineData("```cs\ncode\n```\n``` ```\n{0}\nnew")]
    public void ResetShouldBeHonoredAfterAFenceThatClosesOnItsOwnLine(string template)
    {
        // act
        var markup = Parser.Parse(string.Format(template, Reset));

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetShouldStayCodeInABlockThatFollowsAFenceClosedOnItsOwnLine()
    {
        // The scan and the grammar must agree on which fence is open here
        // act
        var markup = Parser.Parse($"``` ```\n```\n{Reset}\ncode\n```");

        // assert
        markup.Format().Should().Contain(Reset);
    }

    [Fact]
    public void ResetInsideAMultiLineInlineCodeSpanShouldBeHonored()
    {
        // Only fenced blocks protect a marker, so this is a reset like any other
        // act
        var markup = Parser.Parse($"`a\n{Reset}\nb`");

        // assert
        Text(markup).Should().NotContain("a");
    }

    [Fact]
    public void HalfArrivedMarkerInAnOpenCodeBlockShouldBeShownAsCode()
    {
        // act
        var markup = IncompleteParser.Parse("```html\ncode\n<!--re");

        // assert
        markup.Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().EndWith("<!--re");
    }

    [Fact]
    public void ResetAtStartShouldBeDropped()
    {
        // act
        var markup = Parser.Parse($"{Reset}\nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetAtEndShouldLeaveEmptyMarkup()
    {
        // act
        var markup = Parser.Parse($"old\n{Reset}");

        // assert
        markup.Should().Be(MarkupParser.EmptyResult);
    }

    [Fact]
    public void ResetAloneShouldLeaveEmptyMarkup()
    {
        // act
        var markup = Parser.Parse(Reset);

        // assert
        markup.Should().Be(MarkupParser.EmptyResult);
    }

    [Fact]
    public void BlankLinesAfterResetShouldBeDropped()
    {
        // act
        var markup = Parser.Parse($"old\n{Reset}\n\n\nnew");

        // assert
        markup.Should().BeOfType<ParagraphMarkup>();
        Text(markup).Should().Be("new");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\n\n\n\n")]
    [InlineData("\r\n\r\n\r\n")]
    public void ResetShouldEatLineBreaksUpToTheFirstNonEmptyLine(string lineBreaks)
    {
        // act
        var markup = Parser.Parse($"old{lineBreaks}{Reset}{lineBreaks}first line{lineBreaks}second line");

        // assert
        var first = markup is MarkupSeq seq ? seq.Items[0] : markup;
        first.Should().BeOfType<ParagraphMarkup>().Which.IsEmpty.Should().BeFalse("no blank line is left in front");
        Text(markup).Should().StartWith("first line").And.EndWith("second line");
    }

    [Theory]
    [InlineData("```\ncode\n```")]
    [InlineData("```cs\ncode\n```")]
    [InlineData("```\ncode\n  ```  ")]
    [InlineData("```\n```x\ncode\n```")]
    public void ResetShouldBeHonoredAfterAClosedCodeBlock(string codeBlock)
    {
        // act
        var markup = Parser.Parse($"{codeBlock}\n{Reset}\nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetShouldBeKeptWhenTheFenceIsNotClosedByAFenceWithTrailingText()
    {
        // "```x" is a code line, not a closing fence, so the block is still open at the marker
        // act
        var markup = Parser.Parse($"```\ncode\n```x\n{Reset}\nstill code\n```");

        // assert
        markup.Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Contain(Reset);
    }

    [Fact]
    public void ResetShouldTolerateTrailingWhitespace()
    {
        // act
        var markup = Parser.Parse($"old\n{Reset}   \nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetShouldWorkWithCrLf()
    {
        // act
        var markup = Parser.Parse($"old\r\n{Reset}\r\nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetShouldEndTheParagraphAboveIt()
    {
        // The marker line is not a continuation of the paragraph it follows
        // act
        var markup = Parser.Parse($"line one\nline two\n{Reset}\nfresh");

        // assert
        Text(markup).Should().Be("fresh");
    }

    [Fact]
    public void ResetInsideCodeBlockShouldBeKept()
    {
        // arrange
        var text = $"before\n```\n{Reset}\n```\nafter";

        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = markup.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[1].Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Be(Reset);
    }

    [Fact]
    public void ResetInsideCodeBlockWithLanguageShouldBeKept()
    {
        // act
        var markup = Parser.Parse($"```html\na\n{Reset}\nb\n```");

        // assert
        markup.Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Be($"a\r\n{Reset}\r\nb");
    }

    [Fact]
    public void ResetInsideUnclosedCodeBlockShouldBeKept()
    {
        // arrange
        var text = $"before\n```\n{Reset}\nstill code";

        // act
        var markup = IncompleteParser.Parse(text);

        // assert
        markup.Should().BeOfType<MarkupSeq>()
            .Which.Items[1].Should().BeOfType<CodeBlockMarkup>()
            .Which.Code.Should().Contain(Reset);
    }

    [Fact]
    public void ResetAfterCodeBlockShouldDropTheBlock()
    {
        // act
        var markup = Parser.Parse($"```\ncode\n```\n{Reset}\nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetInsideInlineCodeShouldBeKept()
    {
        // act
        var markup = Parser.Parse($"say `{Reset}` to restart");

        // assert
        var seq = markup.Should().BeOfType<ParagraphMarkup>().Subject.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.OfType<PreformattedTextMarkup>().Single().Text.Should().Be(Reset);
    }

    [Theory]
    [InlineData("old {0} new")]
    [InlineData("old\n{0} new")]
    [InlineData("old\nnew {0}")]
    [InlineData("old\n x{0}")]
    public void ResetNotAloneOnItsLineShouldStayText(string template)
    {
        // arrange
        var text = string.Format(template, Reset);

        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Format().Replace("\r\n", "\n").Should().Be(text);
        Text(markup).Should().StartWith("old");
    }

    [Fact]
    public void ResetInsideBlockQuoteShouldNotDropAnything()
    {
        // act
        var markup = Parser.Parse($"old\n\n> quoted\n> {Reset}\n> more");

        // assert
        Text(markup).Should().Contain("old").And.Contain("quoted").And.Contain("more");
    }

    [Fact]
    public void ResetShouldBeDroppedWithUnparsedTextKind()
    {
        // act
        var markup = UnparsedParser.Parse($"old\n{Reset}\nnew");

        // assert
        Text(markup).Should().Be("new");
    }

    [Fact]
    public void ResetShouldBeDroppedByIncompleteParser()
    {
        // act
        var markup = IncompleteParser.Parse($"old **bold**\n{Reset}\nnew **par");

        // assert
        Text(markup).Should().Be("new par");
    }

    [Fact]
    public void ResetShouldSurviveRoundTripAsNothing()
    {
        // The parser never produces a reset: it is a command to the parser, not content
        // act
        var markup = Parser.Parse($"old\n{Reset}\nnew");

        // assert
        markup.Format().Should().NotContain("<!--");
    }

    [Theory]
    [InlineData("<")]
    [InlineData("<!")]
    [InlineData("<!--")]
    [InlineData("<!--res")]
    [InlineData("<!--reset-")]
    [InlineData("<!--reset--")]
    public void HalfArrivedResetShouldBeHiddenWhileStreaming(string partial)
    {
        // act
        var markup = IncompleteParser.Parse($"old\n{partial}");

        // assert
        Text(markup).Should().Be("old");
    }

    [Fact]
    public void HalfArrivedResetAloneShouldLeaveEmptyMarkup()
    {
        // act
        var markup = IncompleteParser.Parse("<!--re");

        // assert
        markup.Should().Be(MarkupParser.EmptyResult);
    }

    [Fact]
    public void HalfArrivedResetShouldBeShownByCompleteParser()
    {
        // A finished message that happens to end like a marker is just text
        // act
        var markup = Parser.Parse("old\n<!--re");

        // assert
        Text(markup).Should().Contain("<!--re");
    }

    [Fact]
    public void PartOfResetInTheMiddleShouldNotBeHiddenWhileStreaming()
    {
        // act
        var markup = IncompleteParser.Parse("old <!--re more text");

        // assert
        Text(markup).Should().Be("old <!--re more text");
    }

    [Fact]
    public void StreamedPrefixesShouldNeverShowTheMarker()
    {
        // arrange
        var full = $"first **part**\n{Reset}\nsecond *part*\n- a\n- b";

        // act & assert
        for (var length = 1; length <= full.Length; length++) {
            var prefixMarkup = IncompleteParser.Parse(full[..length]);
            Text(prefixMarkup).Should().NotContain("<!--", $"a prefix of {length} chars must not show it");
        }
        Text(IncompleteParser.Parse(full)).Should().StartWith("second part").And.NotContain("first");
    }

    // Private methods

    private static string Text(Markup markup)
        => markup.ToReadableText();
}
