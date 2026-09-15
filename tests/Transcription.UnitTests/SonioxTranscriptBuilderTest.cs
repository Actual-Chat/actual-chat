namespace ActualChat.Transcription.UnitTests;

public class SonioxTranscriptBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void FinalTokensShouldAccumulate()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true)], 500);
        var second = builder.Update([Token(" world", 500, 1000, true)], 1000)[^1];

        // assert
        second.Text.Should().Be("Hello world");
    }

    [Fact]
    public void ANonFinalTailShouldBeReplacedNotAppended()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        var first = builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)], 700)[^1];
        var second = builder.Update([Token(" world", 500, 1000, false)], 1000)[^1];

        // assert
        first.Text.Should().Be("Hello wor");
        second.Text.Should().Be("Hello world");
    }

    [Fact]
    public void CompleteShouldDropTheNonFinalTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)], 700);
        var completed = builder.Complete();

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void AnAbnormalEndShouldKeepTheTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)], 700);
        var completed = builder.Complete(false);

        // assert
        completed.Text.Should().Be("Hello wor");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void AnAbnormalEndWithNoTailShouldBeJustTheFinals()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act
        builder.Update([Token("Hello", 0, 500, true)], 500);
        var completed = builder.Complete(false);

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
    }

    [Fact]
    public void FinalsOnlyResponseShouldBeStable()
    {
        // act
        var transcripts = new SonioxTranscriptBuilder().Update([Token("Hello", 0, 500, true)], 500);

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
            .Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)], 700);

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
        builder.Update([Token("Hello", 0, 500, true)], 500);

        // act
        var transcripts = builder.Update([Token(" wor", 500, 700, false)], 700);

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
        builder.Update([Token("Hello", 0, 500, true), Token(" wor", 500, 700, false)], 700);

        // act - the tail became final, and a new tail starts
        var transcripts = builder.Update([Token(" world", 500, 1000, true), Token(" how", 1000, 1200, false)], 1200);

        // assert
        transcripts[0].Text.Should().Be("Hello world");
        transcripts[0].IsStable.Should().BeTrue();
        transcripts[0].TimeRange.End.Should().BeApproximately(1f, 0.01f);
        transcripts[1].Text.Should().Be("Hello world how");
    }

    [Fact]
    public void TimeMapShouldBeBuiltFromTokenTimings()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true), Token(" world", 500, 2000, true)], 2000)[^1];

        // assert
        WriteLine(transcript.TimeMap.ToString());
        transcript.TimeMap.IsDegenerate.Should().BeFalse();
        transcript.TimeRange.End.Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void LanguagesShouldBeCollectedFromTokens()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, true, "en"), Token(" мир", 500, 1000, true, "ru")], 1000)[^1];

        // assert
        transcript.Languages.Should().Contain(Languages.English);
        transcript.Languages.Should().Contain(Languages.Russian);
    }

    [Fact]
    public void EndpointMarkersShouldNeverReachTheTranscript()
    {
        // arrange - enable_endpoint_detection emits "<end>" once per finalized segment
        var builder = new SonioxTranscriptBuilder();

        // act
        var update = builder.Update([Token("Проверка", 0, 500, true), Token("<end>", 500, 520, true)], 520)[^1];
        var completed = builder.Complete();

        // assert
        update.Text.Should().Be("Проверка");
        completed.Text.Should().Be("Проверка");
    }

    [Fact]
    public void AnEndpointMarkerInTheTailShouldBeDroppedToo()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("Hi", 0, 500, true), Token("<end>", 500, 520, false)], 520)[^1];

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void EmptyTokensShouldBeSkipped()
    {
        // act
        var transcript = new SonioxTranscriptBuilder()
            .Update([Token("", 0, 100, true), Token("Hi", 100, 500, true)], 500)[^1];

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void AnOldNonFinalTokenShouldBePromotedToStable()
    {
        // act - "Hello" ended 3 s before the processed position, " world" ended 0.5 s before it
        var transcripts = new SonioxTranscriptBuilder()
            .Update([Token("Hello", 0, 500, false), Token(" world", 500, 3000, false)], 3500);

        // assert
        transcripts.Should().HaveCount(2);
        transcripts[0].Text.Should().Be("Hello");
        transcripts[0].IsStable.Should().BeTrue("a non-final token older than StableTokenAge is as good as final");
        transcripts[0].TimeRange.End.Should().BeApproximately(0.5f, 0.001f);
        transcripts[1].Text.Should().Be("Hello world");
        transcripts[1].IsStable.Should().BeFalse();
    }

    [Fact]
    public void AYoungNonFinalTokenShouldStayInTheTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act - the token ended 2 s before the processed position, under StableTokenAge
        var transcripts = builder.Update([Token("Hello", 0, 500, false)], 2500);
        var completed = builder.Complete();

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].IsStable.Should().BeFalse();
        completed.Text.Should().BeEmpty("nothing was final or old enough to be promoted");
    }

    [Fact]
    public void APromotedSpanShouldNotBeAppendedAgain()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();
        builder.Update([Token("Hello", 0, 500, false)], 3000);

        // act - Soniox re-sends the whole non-final tail, then finalizes it
        var resent = builder.Update([Token("Hello", 0, 500, false), Token(" world", 500, 800, false)], 3200);
        var finalized = builder.Update([Token("Hello", 0, 500, true), Token(" world", 500, 800, true)], 3500);
        var completed = builder.Complete();

        // assert
        resent.Should().ContainSingle();
        resent[0].Text.Should().Be("Hello world");
        resent[0].IsStable.Should().BeFalse();
        finalized.Should().ContainSingle();
        finalized[0].Text.Should().Be("Hello world");
        finalized[0].IsStable.Should().BeTrue();
        finalized[0].TimeMap.Length.Should().Be(3, "the map holds one point per token boundary: 0, 0.5, 0.8");
        finalized[0].TimeRange.End.Should().BeApproximately(0.8f, 0.001f);
        completed.Text.Should().Be("Hello world");
    }

    [Fact]
    public void ARevisionOfAPromotedWordShouldBeIgnored()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();
        builder.Update([Token("Hello", 0, 500, false)], 3000);

        // act
        var revised = builder.Update([Token("Hallo", 0, 500, false)], 3100);
        var completed = builder.Complete();

        // assert
        revised.Should().BeEmpty("the promoted span is settled, so a revision of it changes nothing");
        completed.Text.Should().Be("Hello");
    }

    [Fact]
    public void ATokenStraddlingTheBoundaryShouldStayInTheTail()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();

        // act - the stable boundary is at 500 ms, the token spans 0..600 ms
        var transcripts = builder.Update([Token("Hello", 0, 600, false)], 3000);
        var completed = builder.Complete();

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].IsStable.Should().BeFalse();
        completed.Text.Should().BeEmpty();
    }

    [Fact]
    public void CompleteAfterPromotionShouldReturnThePromotedTextAsStable()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder();
        builder.Update([Token("Hello", 0, 500, false), Token(" world", 500, 2900, false)], 3000);

        // act
        var completed = builder.Complete();

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
        completed.TimeRange.End.Should().BeApproximately(0.5f, 0.001f);
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
