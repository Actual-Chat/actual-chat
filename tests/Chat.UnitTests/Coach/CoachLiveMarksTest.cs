namespace ActualChat.Chat.UnitTests.Coach;

public class CoachLiveMarksTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static SpeechSpan Span(SpeechSpanKind kind, string word, int start)
        => new (kind, word, start, word.Length, ApiArray<string>.Empty);

    [Fact]
    public void FromSpansShouldCountTheOccurrenceOfTheWordInTheWholeText()
    {
        // arrange
        const string text = "um yes um no um";
        var spans = new[] { Span(SpeechSpanKind.Filler, "um", 7), Span(SpeechSpanKind.Filler, "um", 13) };

        // act
        var marks = CoachLiveMarks.FromSpans(text, spans, true);

        // assert
        marks.Select(m => m.Occurrence).Should().Equal(2, 3);
    }

    [Fact]
    public void LocateShouldFindTheSameWordsInATextThatChangedALittle()
    {
        // arrange: the tagged text and the text the client shows differ in case and punctuation
        var marks = CoachLiveMarks.FromSpans(
            "so um I think you know it works",
            [Span(SpeechSpanKind.FilledPause, "um", 3), Span(SpeechSpanKind.Filler, "you know", 14)],
            true);
        const string shown = "So, um, I think, you know, it works.";

        // act
        var spans = CoachLiveMarks.Locate(shown, marks, true);

        // assert
        spans.Select(s => shown.Substring(s.Start, s.Length)).Should().Equal("um", "you know");
        spans.Select(s => s.Kind).Should().Equal(SpeechSpanKind.FilledPause, SpeechSpanKind.Filler);
    }

    [Fact]
    public void LocateShouldSkipAMarkTheTextDoesNotHaveYet()
    {
        // arrange
        var marks = ApiArray.New(
            new CoachLiveMark(SpeechSpanKind.Filler, "like", 2, ApiArray<string>.Empty),
            new CoachLiveMark(SpeechSpanKind.Filler, "like", 1, ApiArray<string>.Empty));

        // act
        var spans = CoachLiveMarks.Locate("it was like this", marks, true);

        // assert
        spans.Should().ContainSingle().Which.Start.Should().Be(7);
    }

    [Fact]
    public void LocateShouldMatchInsideTheTextForScriptsWithoutWordSpaces()
    {
        // arrange
        var marks = ApiArray.New(new CoachLiveMark(SpeechSpanKind.FilledPause, "えっと", 1, ApiArray<string>.Empty));

        // act
        var spans = CoachLiveMarks.Locate("私はえっと店に行きました", marks, false);

        // assert
        spans.Should().ContainSingle().Which.Start.Should().Be(2);
    }
}
