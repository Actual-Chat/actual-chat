using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachStreamingMarksTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static SpeechSpan Span(SpeechSpanKind kind, int start, int length)
        => new (kind, "x", start, length, ApiArray<string>.Empty);

    [Fact]
    public void SplitShouldReturnTheWholeSegmentWhenThereAreNoMarks()
    {
        // act
        var pieces = CoachStreamingMarks.Split("hello there", 0, ApiArray<SpeechSpan>.Empty);

        // assert
        pieces.Should().Equal(new CoachStreamingMarks.Piece("hello there", ""));
    }

    [Fact]
    public void SplitShouldCutAMarkOutOfTheMiddle()
    {
        // arrange
        var spans = ApiArray.New(Span(SpeechSpanKind.FilledPause, 3, 2));

        // act
        var pieces = CoachStreamingMarks.Split("So, um, ok", 0, spans);

        // assert
        pieces.Select(p => (p.Text, p.Class)).Should().Equal(("So,", ""), (" u", "coach-filler"), ("m, ok", ""));
    }

    [Fact]
    public void SplitShouldUseTheSegmentOffsetAndClipAMarkThatStraddlesTheBoundary()
    {
        // arrange: the full text is "So, um, ok"; a mark covers "um," at 4..7 and the segment starts at 5
        var spans = ApiArray.New(Span(SpeechSpanKind.Filler, 4, 3));

        // act
        var pieces = CoachStreamingMarks.Split("m, ok", 5, spans);

        // assert
        pieces.Select(p => (p.Text, p.Class)).Should().Equal(("m,", "coach-filler"), (" ok", ""));
    }

    [Fact]
    public void SplitShouldGiveWeakWordsTheirOwnClassAndIgnoreMarksOutsideTheSegment()
    {
        // arrange
        var spans = ApiArray.New(
            Span(SpeechSpanKind.Filler, 0, 2),
            Span(SpeechSpanKind.Weak, 10, 4),
            Span(SpeechSpanKind.Repetition, 30, 3));

        // act
        var pieces = CoachStreamingMarks.Split("very nice job", 8, spans);

        // assert
        pieces.Select(p => (p.Text, p.Class)).Should().Equal(("ve", ""), ("ry n", "coach-weak"), ("ice job", ""));
    }

    [Fact]
    public void SplitShouldReturnNothingForAnEmptySegment()
        => CoachStreamingMarks.Split("", 0, ApiArray.New(Span(SpeechSpanKind.Filler, 0, 2))).Should().BeEmpty();
}
