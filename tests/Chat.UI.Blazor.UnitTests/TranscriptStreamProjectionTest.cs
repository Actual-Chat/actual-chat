using ActualChat.Transcription;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class TranscriptStreamProjectionTest
{
    [Fact]
    public void AStablePrefixShouldNotEraseTheShownTail()
    {
        // arrange - Soniox settles "one two" while " three" is still in the tail: the stable
        // finals-only transcript precedes the unstable whole
        var projection = new TranscriptStreamProjection("", false);
        projection.Next(Unstable("one two three"));

        // act
        var onStable = projection.Next(Stable("one two"));
        var onTail = projection.Next(Unstable("one two three"));

        // assert
        onStable.Should().BeNull("the tail is about to be re-sent, not retracted");
        onTail!.Text.Should().Be("one two three");
        onTail.ChangedText.Should().BeEmpty("nothing changed on screen");
        onTail.AnimatedText.Should().BeEmpty("nothing changed on screen");
    }

    [Fact]
    public void ARevisedTailShouldReplaceTheShownOne()
    {
        // arrange
        var projection = new TranscriptStreamProjection("", false);
        projection.Next(Unstable("one two tree"));
        projection.Next(Stable("one two"));

        // act
        var state = projection.Next(Unstable("one two three"));

        // assert
        state!.RetainedText.Should().Be("one two t");
        state.Text.Should().Be("one two three");
    }

    [Fact]
    public void CompletionShouldShowTheLastTranscriptNotTheHeldTail()
    {
        // arrange - the stream ends with the stable finals; an unfinalized tail is dropped for real
        var projection = new TranscriptStreamProjection("", false);
        projection.Next(Unstable("one two three"));
        projection.Next(Stable("one two"));

        // act
        var state = projection.Complete();

        // assert
        state.Text.Should().Be("one two");
        state.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public void AGrowingTranscriptShouldAnimateTheAppendedText()
    {
        // arrange
        var projection = new TranscriptStreamProjection("", false);
        projection.Next(Unstable("one"));

        // act
        var state = projection.Next(Unstable("one two"));

        // assert
        state!.RetainedText.Should().Be("one");
        state.AnimatedText.Should().Be(" two");
    }

    private static Transcript Unstable(string text)
        => Transcript.New() with { Text = text, IsStable = false };

    private static Transcript Stable(string text)
        => Transcript.New() with { Text = text, IsStable = true };
}
