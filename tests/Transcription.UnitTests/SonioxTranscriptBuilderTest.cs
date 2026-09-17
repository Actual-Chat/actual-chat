namespace ActualChat.Transcription.UnitTests;

public class SonioxTranscriptBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TimeSpan StableTokenAge = TimeSpan.FromSeconds(1.5);

    [Fact]
    public void TheAgeIsTheBuildersArgument()
    {
        var builder = new SonioxTranscriptBuilder(TimeSpan.FromMilliseconds(300));

        var transcripts = builder.Update([Token("Hello", 0, 500, false)], 800);

        transcripts.Should().ContainSingle();
        transcripts[0].IsStable.Should().BeTrue("500 ms + 300 ms age <= 800 ms processed");
    }

    [Fact]
    public void FinalTokensShouldAccumulate()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var transcripts = new SonioxTranscriptBuilder(StableTokenAge).Update([Token("Hello", 0, 500, true)], 500);

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].Text.Should().Be("Hello");
        transcripts[0].IsStable.Should().BeTrue("a final token never changes, so the finals alone are stable");
    }

    [Fact]
    public void FinalsWithATailShouldEmitTheStableFinalsBeforeTheUnstableWhole()
    {
        // act
        var transcripts = new SonioxTranscriptBuilder(StableTokenAge)
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
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
        var transcript = new SonioxTranscriptBuilder(StableTokenAge)
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
        var transcript = new SonioxTranscriptBuilder(StableTokenAge)
            .Update([Token("Hello", 0, 500, true, "en"), Token(" мир", 500, 1000, true, "ru")], 1000)[^1];

        // assert
        transcript.Languages.Should().Contain(Languages.English);
        transcript.Languages.Should().Contain(Languages.Russian);
    }

    [Fact]
    public void LanguagesShouldComeFromSettledTokensOnly()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

        // act - a tail token tagged "en" is retracted by the next message, whose final is tagged "ru"
        var withTail = builder
            .Update([Token("Привет", 0, 500, true, "ru"), Token(" wor", 500, 700, false, "en")], 700)[^1];
        var settled = builder.Update([Token(" мир", 500, 1000, true, "ru")], 1000)[^1];

        // assert
        withTail.Languages.Should().Equal([Languages.Russian], "a retractable tail tag is not a language heard");
        settled.Languages.Should().Equal([Languages.Russian]);
        settled.Text.Should().Be("Привет мир");
    }

    [Fact]
    public void EndpointMarkersShouldNeverReachTheTranscript()
    {
        // arrange - enable_endpoint_detection emits "<end>" once per finalized segment
        var builder = new SonioxTranscriptBuilder(StableTokenAge);

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
        var transcript = new SonioxTranscriptBuilder(StableTokenAge)
            .Update([Token("Hi", 0, 500, true), Token("<end>", 500, 520, false)], 520)[^1];

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void AnEndpointWithFinalsShouldFlagTheStableTranscript()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        builder.Update([Token("Да", 500, 1000, false)], 1000);

        // act
        var transcripts = builder.Update([Token("Да", 500, 1080, true), Token("<end>", 1080, 1100, true)], 1100);

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].IsStable.Should().BeTrue();
        transcripts[0].IsSegmentEnd.Should().BeTrue("the endpoint is the transcriber's end-of-utterance call");
    }

    [Fact]
    public void AnEndpointAloneShouldReEmitTheFlaggedStableTranscript()
    {
        // arrange - the finals came a message earlier, so the endpoint's message brings no new text
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var finals = builder.Update([Token("Да", 500, 1080, true)], 1100)[^1];

        // act
        var transcripts = builder.Update([Token("<end>", 1080, 1100, true)], 1200);

        // assert
        finals.IsSegmentEnd.Should().BeFalse();
        transcripts.Should().ContainSingle("the signal must not be lost with the message that carries no text");
        transcripts[0].Text.Should().Be("Да");
        transcripts[0].IsStable.Should().BeTrue();
        transcripts[0].IsSegmentEnd.Should().BeTrue();
        transcripts[0].TimeRange.End.Should().BeApproximately(1.08f, 0.001f);
    }

    [Fact]
    public void AnEndpointWithNoFinalsYetShouldEmitNothing()
    {
        // act
        var transcripts = new SonioxTranscriptBuilder(StableTokenAge).Update([Token("<end>", 0, 20, true)], 20);

        // assert
        transcripts.Should().BeEmpty("an empty stable transcript would rewind the readers to nothing");
    }

    [Fact]
    public void TheTranscriptsAfterAnEndpointShouldNotBeFlagged()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        builder.Update([Token("Да", 500, 1080, true), Token("<end>", 1080, 1100, true)], 1100);

        // act - new speech: a tail, then its finals, then the stream ends
        var tail = builder.Update([Token(" Как", 1500, 1800, false)], 1800)[^1];
        var finals = builder.Update([Token(" Как", 1500, 1800, true)], 1900)[^1];
        var completed = builder.Complete();

        // assert
        tail.IsSegmentEnd.Should().BeFalse("the flag is per transcript, like IsStable");
        finals.IsSegmentEnd.Should().BeFalse();
        completed.IsSegmentEnd.Should().BeFalse("the stream end is its own signal");
    }

    [Fact]
    public void ATailInTheEndpointsMessageShouldFollowTheFlaggedFinalsUnflagged()
    {
        // act - the next segment's first tail token rides the same message as the endpoint
        var transcripts = new SonioxTranscriptBuilder(StableTokenAge).Update(
            [Token("Да", 500, 1080, true), Token("<end>", 1080, 1100, true), Token(" Как", 1500, 1800, false)],
            1800);

        // assert
        transcripts.Should().HaveCount(2);
        transcripts[0].Text.Should().Be("Да");
        transcripts[0].IsSegmentEnd.Should().BeTrue();
        transcripts[1].Text.Should().Be("Да Как");
        transcripts[1].IsSegmentEnd.Should().BeFalse();
    }

    [Fact]
    public void EmptyTokensShouldBeSkipped()
    {
        // act
        var transcript = new SonioxTranscriptBuilder(StableTokenAge)
            .Update([Token("", 0, 100, true), Token("Hi", 100, 500, true)], 500)[^1];

        // assert
        transcript.Text.Should().Be("Hi");
    }

    [Fact]
    public void AnOldNonFinalTokenShouldBePromotedToStable()
    {
        // act - "Hello" ended 3 s before the processed position, " world" ended 0.5 s before it
        var transcripts = new SonioxTranscriptBuilder(StableTokenAge)
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var stableTokenAgeMs = (long)StableTokenAge.TotalMilliseconds;

        // act - the token ended just under StableTokenAge before the processed position
        var transcripts = builder.Update([Token("Hello", 0, 500, false)], 500 + stableTokenAgeMs - 1);
        var completed = builder.Complete();

        // assert
        transcripts.Should().ContainSingle();
        transcripts[0].IsStable.Should().BeFalse();
        completed.Text.Should().BeEmpty("nothing was final or old enough to be promoted");
    }

    [Fact]
    public void APromotedSpanShouldNotBeAppendedAgain()
    {
        // arrange - the boundary sits exactly at "Hello"'s end, so it promotes; 200 ms later
        // it's still 100 ms short of " world"'s end, so that one stays in the tail
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var stableTokenAgeMs = (long)StableTokenAge.TotalMilliseconds;
        var promotedAtMs = 500 + stableTokenAgeMs;
        builder.Update([Token("Hello", 0, 500, false)], promotedAtMs);

        // act - Soniox re-sends the whole non-final tail, then finalizes it
        var resent = builder.Update([Token("Hello", 0, 500, false), Token(" world", 500, 800, false)],
            promotedAtMs + 200);
        var finalized = builder.Update([Token("Hello", 0, 500, true), Token(" world", 500, 800, true)],
            promotedAtMs + 500);
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var stableTokenAgeMs = (long)StableTokenAge.TotalMilliseconds;

        // act - the stable boundary is 100 ms before the token's end, which spans 0..600 ms
        var transcripts = builder.Update([Token("Hello", 0, 600, false)], 500 + stableTokenAgeMs);
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
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        builder.Update([Token("Hello", 0, 500, false), Token(" world", 500, 2900, false)], 3000);

        // act
        var completed = builder.Complete();

        // assert
        completed.Text.Should().Be("Hello");
        completed.IsStable.Should().BeTrue();
        completed.TimeRange.End.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void FoldedDiffsShouldKeepTheEndTimeOfAShortUtterance()
    {
        // arrange - a tail, then its finals, then a silent finish: the sequence a one-word utterance yields
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var transcripts = new List<Transcript>();
        transcripts.AddRange(builder.Update([Token("Да", 500, 1000, false)], 1000));
        transcripts.AddRange(builder.Update([Token("Да", 500, 1080, true), Token("<end>", 1080, 1100, true)], 1100));
        transcripts.Add(builder.Complete());

        // act
        var folded = FoldThroughDiffs(transcripts);

        // assert
        folded.Text.Should().Be("Да");
        folded.IsStable.Should().BeTrue();
        folded.TimeRange.End.Should().BeApproximately(transcripts[^1].TimeRange.End, Transcript.TimeMapEpsilon.Y);
    }

    [Fact]
    public void FoldedDiffsShouldKeepTheEndTimeAfterAFinalsOnlyRepeat()
    {
        // arrange - the finals transcript is emitted twice in a row (the endpoint re-emits it), then completed
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var transcripts = new List<Transcript>();
        transcripts.AddRange(builder.Update([Token("Да", 500, 1000, false)], 1000));
        transcripts.AddRange(builder.Update([Token("Да", 500, 1080, true)], 1100));
        transcripts.Add(builder.Complete());
        transcripts.Add(builder.Complete());

        // act
        var folded = FoldThroughDiffs(transcripts);

        // assert
        folded.Text.Should().Be("Да");
        folded.TimeRange.End.Should().BeApproximately(1.08f, Transcript.TimeMapEpsilon.Y);
    }

    [Fact]
    public void FoldedDiffsShouldKeepTheEndTimeAfterARewind()
    {
        // arrange - the tail ran ahead of what got finalized: "Да я" is rewound to "Да."
        var builder = new SonioxTranscriptBuilder(StableTokenAge);
        var transcripts = new List<Transcript>();
        transcripts.AddRange(builder.Update([Token("Да", 500, 1000, false), Token(" я", 1100, 1300, false)], 1300));
        transcripts.AddRange(builder.Update([Token("Да.", 500, 1050, true)], 1400));
        transcripts.Add(builder.Complete());

        // act
        var folded = FoldThroughDiffs(transcripts);

        // assert
        folded.Text.Should().Be("Да.");
        folded.TimeRange.End.Should().BeApproximately(1.05f, 0.001f);
    }

    // Private methods

    private Transcript FoldThroughDiffs(IReadOnlyList<Transcript> transcripts)
    {
        var folded = Transcript.Empty;
        foreach (var diff in transcripts.ToTranscriptDiffs()) {
            folded += diff;
            WriteLine($"{diff} -> {folded} stable={folded.IsStable}");
        }
        return folded;
    }

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
