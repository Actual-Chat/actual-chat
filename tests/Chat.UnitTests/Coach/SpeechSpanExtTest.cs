namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechSpanExtTest
{
    private static PlayableTextMarkup Markup(string text)
        => new (text, new LinearMap(0, 0, text.Length, 10));

    private static SpeechSpan Span(SpeechSpanKind kind, string text, string word, int occurrence = 1)
    {
        var start = -1;
        for (var i = 0; i < occurrence; i++)
            start = text.IndexOf(word, start + 1);
        return new SpeechSpan(kind, word, start, word.Length, ApiArray<string>.Empty);
    }

    [Fact]
    public void MapToWordsShouldMarkTheWordContainingEachSpan()
    {
        // arrange
        const string text = "So, um, I went to the the store.";
        var markup = Markup(text);
        var spans = new[] {
            Span(SpeechSpanKind.FilledPause, text, "um"),
            Span(SpeechSpanKind.Repetition, text, "the", 2),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds.Should().NotBeNull();
        kinds!.Length.Should().Be(markup.Words.Length);
        kinds[1]!.Kind.Should().Be(SpeechSpanKind.FilledPause, "'um,' is the second word");
        kinds[6]!.Kind.Should().Be(SpeechSpanKind.Repetition, "the repeated 'the' is the seventh word");
        kinds.Count(k => k is not null).Should().Be(2);
    }

    [Fact]
    public void MapToWordsShouldIgnoreSpansOutsideTheText()
    {
        // arrange
        const string text = "short text";
        var markup = Markup(text);
        var spans = new[] {
            new SpeechSpan(SpeechSpanKind.Weak, "gone", 40, 4, ApiArray<string>.Empty),
            new SpeechSpan(SpeechSpanKind.Weak, "x", -1, 1, ApiArray<string>.Empty),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds.Should().BeNull("a span the text no longer holds maps to nothing");
    }

    [Fact]
    public void MapToWordsShouldKeepTheFirstKindWhenTwoSpansHitOneWord()
    {
        // arrange
        const string text = "like like";
        var markup = Markup(text);
        var spans = new[] {
            Span(SpeechSpanKind.Filler, text, "like", 2),
            Span(SpeechSpanKind.Repetition, text, "like", 2),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds![1]!.Kind.Should().Be(SpeechSpanKind.Filler);
    }

    [Fact]
    public void MapToWordsShouldReturnNullForNoSpans()
    {
        // act
        var kinds = Array.Empty<SpeechSpan>().MapToWords(Markup("a b"));

        // assert
        kinds.Should().BeNull();
    }

    [Fact]
    public void MapToWordsShouldMarkEveryWordOfAPhrase()
    {
        // arrange
        const string text = "I went, you know, to the store.";
        var markup = Markup(text);
        var spans = new[] { Span(SpeechSpanKind.Filler, text, "you know") };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds![2]!.Kind.Should().Be(SpeechSpanKind.Filler, "'you' starts the phrase");
        kinds[3]!.Kind.Should().Be(SpeechSpanKind.Filler, "'know,' ends the phrase");
        kinds.Count(k => k is not null).Should().Be(2);
    }

    [Fact]
    public void MapToWordsShouldAttributeAStartInsideTrailingWhitespaceToThatWord()
    {
        // arrange - a stale span may land on the space after a word; it belongs to that word, not the next
        const string text = "So, um, I went.";
        var markup = Markup(text);
        var spans = new[] { new SpeechSpan(SpeechSpanKind.Weak, "x", 3, 1, ApiArray<string>.Empty) };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds![0]!.Kind.Should().Be(SpeechSpanKind.Weak);
    }

    [Fact]
    public void MapToWordsShouldKeepTheSpanSoItsSynonymsReachTheWord()
    {
        // arrange
        const string text = "It was awesome.";
        var markup = Markup(text);
        var spans = new[] {
            new SpeechSpan(SpeechSpanKind.Weak, "awesome", text.IndexOf("awesome"), 7, ApiArray.New("excellent")),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds![2]!.Synonyms.Should().Equal("excellent");
    }
}
