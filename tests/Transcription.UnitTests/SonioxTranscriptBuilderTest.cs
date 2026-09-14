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
        var second = builder.Update([Token(" world", 500, 1000, true)])[^1];

        // assert
        second.Text.Should().Be("Hello world");
    }

    [Fact]
    public void NonFinalTailIsReplacedNotAppended()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        var first = builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)])[^1];
        var second = builder.Update([Token(" world", 500, 1000, false)])[^1];

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
    public void FinalsOnlyResponseShouldBeStable()
    {
        // act
        var transcripts = new SonioxTranscriptBuilder().Update([Token("Hello", 0, 500, true)]);

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].Text.Should().Be("Hello");
        transcripts[0].IsStable.Should().BeTrue("a final token never changes, so the finals alone are stable");
    }

    [Fact]
    public void FinalsWithATailShouldEmitTheStableFinalsBeforeTheUnstableWhole()
    {
        // act
        var transcripts = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)]);

        // assert
        transcripts.Should().HaveCount(2);
        transcripts[0].Text.Should().Be("Hello");
        transcripts[0].IsStable.Should().BeTrue();
        transcripts[1].Text.Should().Be("Hello wor");
        transcripts[1].IsStable.Should().BeFalse("the tail may still change");
    }

    [Fact]
    public void ATailOnlyResponseShouldNotRepeatTheStableFinals()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();
        builder.Update([Token("Hello", 0, 500, true)]);

        // act
        var transcripts = builder.Update([Token(" wor", 500, 700, false)]);

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].Text.Should().Be("Hello wor");
        transcripts[0].IsStable.Should().BeFalse();
    }

    [Fact]
    public void StableFinalsShouldGrowByTheNewFinalsOnly()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)]);

        // act - the tail became final, and a new tail starts
        var transcripts = builder.Update([Token(" world", 500, 1000, true), Token(" how", 1000, 1200, false)]);

        // assert
        transcripts[0].Text.Should().Be("Hello world");
        transcripts[0].IsStable.Should().BeTrue();
        transcripts[0].TimeRange.End.Should().BeApproximately(1f, 0.01f);
        transcripts[1].Text.Should().Be("Hello world how");
    }

    [Fact]
    public void TimeMapIsBuiltFromTokenTimings()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true), Token(" world", 500, 2000, true)])[^1];

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
            .Update([Token("Hello", 0, 500, true, "en"), Token(" мир", 500, 1000, true, "ru")])[^1];

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
        var update = builder.Update([Token("Проверка", 0, 500, true), Token("<end>", 500, 520, true)])[^1];
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
            .Update([Token("Hi", 0, 500, true), Token("<end>", 500, 520, false)])[^1];

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void EmptyTokensAreSkipped()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("", 0, 100, true), Token("Hi", 100, 500, true)])[^1];

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
