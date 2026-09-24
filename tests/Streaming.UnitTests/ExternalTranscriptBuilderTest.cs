using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public sealed class ExternalTranscriptBuilderTest
{
    [Fact]
    public void ShouldAppendTextAndUseTheSuppliedOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act
        var diff = builder.Append(new ExternalTranscriptChunk("Hello", true, 1.0, true), TimeSpan.Zero);

        // assert
        diff.Should().NotBeNull();
        builder.Transcript.Text.Should().Be("Hello");
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(1.0f, 0.001f);
        builder.Transcript.TimeMap.XRange.End.Should().Be(5);
    }

    [Fact]
    public void ShouldDeriveTheOffsetFromIngestedAudioWhenAbsent()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act - no offset supplied, but 2s of audio has already arrived
        builder.Append(new ExternalTranscriptChunk("Hello", true, null, true), TimeSpan.FromSeconds(2));

        // assert
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public void ShouldReplaceTheWholeTextWhenNotAppending()
    {
        // arrange - a recognizer correcting itself
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Helo wrld", true, 1.0, false), TimeSpan.Zero);

        // act
        builder.Append(new ExternalTranscriptChunk("Hello world", false, 1.2, true), TimeSpan.Zero);

        // assert
        builder.Transcript.Text.Should().Be("Hello world");
    }

    [Fact]
    public void ShouldClampADecreasingOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, 2.0, true), TimeSpan.Zero);

        // act - a producer that goes backwards must not invert the map
        builder.Append(new ExternalTranscriptChunk("two", true, 1.0, true), TimeSpan.Zero);

        // assert
        builder.Transcript.TimeMap.IsValid().Should().BeTrue();
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public void ShouldRejectANonFiniteOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act
        var append = () => builder.Append(
            new ExternalTranscriptChunk("x", true, double.NaN, true), TimeSpan.Zero);

        // assert
        append.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldTruncateTheMapWhenAReplacementShrinksTheText()
    {
        // arrange - three mapped chunks, then a correction far shorter than all of them
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, 1.0, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("two ", true, 2.0, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("three", true, 3.0, true), TimeSpan.Zero);

        // act
        builder.Append(new ExternalTranscriptChunk("Hi", false, 3.5, true), TimeSpan.Zero);

        // assert
        builder.Transcript.Text.Should().Be("Hi");
        builder.Transcript.TimeMap.XRange.End.Should().Be(2,
            "no map point may sit past the end of the text");
        builder.Transcript.TimeMap.IsValid().Should().BeTrue();
    }

    [Fact]
    public void ShouldSpreadADegenerateMapAcrossTheFinalDuration()
    {
        // arrange - all text first, all audio last: every derived offset is zero
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, null, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("two ", true, null, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("three", true, null, true), TimeSpan.Zero);

        // act
        var transcript = builder.Finalize(TimeSpan.FromSeconds(6));

        // assert
        transcript.TimeMap.IsDegenerate.Should().BeFalse();
        transcript.TimeMap.YRange.End.Should().BeApproximately(6.0f, 0.001f);
        transcript.TimeMap.IsValid().Should().BeTrue();
    }

    [Fact]
    public void ShouldFinalizeWithoutAudioDuration()
    {
        // arrange - header-only Ogg: no frames, so no duration
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Hello", true, null, true), TimeSpan.Zero);

        // act
        var transcript = builder.Finalize(TimeSpan.Zero);

        // assert
        transcript.Text.Should().Be("Hello");
        transcript.TimeMap.Data.Should().OnlyContain(v => float.IsFinite(v));
    }

    [Fact]
    public void ShouldRejectAnOffsetPastTheFinalDuration()
    {
        // arrange - the one case where the producer is wrong about its own media
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Hello", true, 30.0, true), TimeSpan.Zero);

        // act
        var finalize = () => builder.Finalize(TimeSpan.FromSeconds(5));

        // assert
        finalize.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldHandleLlmSizedFragments()
    {
        // arrange - an LLM emits a token at a time, each with the audio spoken so far
        var builder = new ExternalTranscriptBuilder();
        var fragments = new[] { "The ", "quick ", "brown ", "fox " };

        // act
        for (var i = 0; i < fragments.Length; i++)
            builder.Append(new ExternalTranscriptChunk(fragments[i], true, null, true),
                TimeSpan.FromSeconds(0.5 * (i + 1)));
        var transcript = builder.Finalize(TimeSpan.FromSeconds(2));

        // assert
        transcript.Text.Should().Be("The quick brown fox ");
        transcript.TimeMap.IsValid().Should().BeTrue();
        transcript.TimeMap.Length.Should().BeGreaterThan(2, "each fragment should be seekable");
    }

    [Fact]
    public void ShouldHandleRecognizerCorrections()
    {
        // arrange - a recognizer replaces its whole hypothesis repeatedly
        var builder = new ExternalTranscriptBuilder();

        // act
        builder.Append(new ExternalTranscriptChunk("I scream", false, 1.0, false), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("ice cream", false, 1.1, false), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("ice cream cone", false, 2.0, true), TimeSpan.Zero);
        var transcript = builder.Finalize(TimeSpan.FromSeconds(2));

        // assert
        transcript.Text.Should().Be("ice cream cone");
        transcript.TimeMap.IsValid().Should().BeTrue();
    }

    [Fact]
    public void ShouldCarryTheDeclaredLanguage()
    {
        // Nothing reads the words here, so an undeclared transcript names no language - and a
        // listener is never offered a translation of a message whose language is unknown.

        // arrange
        var builder = new ExternalTranscriptBuilder(Languages.German);

        // act
        builder.Append(new ExternalTranscriptChunk("Guten Tag", true, 0.5, true), TimeSpan.Zero);
        var transcript = builder.Finalize(TimeSpan.FromSeconds(1));

        // assert
        transcript.Languages.Should().Equal(Languages.German);
    }
}
