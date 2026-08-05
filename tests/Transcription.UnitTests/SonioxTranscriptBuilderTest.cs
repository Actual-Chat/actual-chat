using ActualChat.Transcription;

namespace ActualChat.Transcription.UnitTests;

public class SonioxTranscriptBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void FinalTokensAccumulate()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true)]);
        var second = builder.Update([Token(" world", 500, 1000, true)]);

        // assert
        second.Text.Should().Be("Hello world");
    }

    [Fact]
    public void NonFinalTailIsReplacedNotAppended()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        var first = builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)]);
        var second = builder.Update([Token(" world", 500, 1000, false)]);

        // assert
        first.Text.Should().Be("Hello wor");
        second.Text.Should().Be("Hello world");
    }

    [Fact]
    public void CompleteDropsTheNonFinalTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)]);
        var completed = builder.Complete();

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void AnAbnormalEndKeepsTheTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)]);
        var completed = builder.Complete(false);

        // assert
        completed.Text.Should().Be("Hello wor");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void AnAbnormalEndWithNoTailIsJustTheFinals()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true)]);
        var completed = builder.Complete(false);

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void StreamingTranscriptsAreNotStable()
    {
        // act
        var transcript = new SonioxTranscriptBuilder().Update([Token("Hello", 0, 500, true)]);

        // assert
        transcript.IsStable.Should().BeFalse();
    }

    [Fact]
    public void TimeMapIsBuiltFromTokenTimings()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true), Token(" world", 500, 2000, true)]);

        // assert
        WriteLine(transcript.TimeMap.ToString());
        transcript.TimeMap.IsDegenerate.Should().BeFalse();
        transcript.TimeRange.End.Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void LanguagesAreCollectedFromTokens()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true, "en"), Token(" мир", 500, 1000, true, "ru")]);

        // assert
        transcript.Languages.Should().Contain(Languages.English);
        transcript.Languages.Should().Contain(Languages.Russian);
    }

    [Fact]
    public void EndpointMarkersNeverReachTheTranscript()
    {
        // arrange - enable_endpoint_detection emits "<end>" once per finalized segment
        var builder = new SonioxTranscriptBuilder();

        // act
        var update = builder.Update([Token("Проверка", 0, 500, true), Token("<end>", 500, 520, true)]);
        var completed = builder.Complete();

        // assert
        update.Text.Should().Be("Проверка");
        completed.Text.Should().Be("Проверка");
    }

    [Fact]
    public void EndpointMarkerInTheTailIsDroppedToo()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hi", 0, 500, true), Token("<end>", 500, 520, false)]);

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void EmptyTokensAreSkipped()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("", 0, 100, true), Token("Hi", 100, 500, true)]);

        // assert
        transcript.Text.Should().Be("Hi");
    }

    // Private methods

    private static SonioxToken Token(
        string text,
        long startMs,
        long endMs,
        bool isFinal,
        string? language = null)
        => new() {
            Text = text,
            StartMs = startMs,
            EndMs = endMs,
            IsFinal = isFinal,
            Language = language,
        };
}
