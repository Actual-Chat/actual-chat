using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class DubStabilizerTest
{
    [Fact]
    public void NextShouldSkipUnstableTranscripts()
    {
        // arrange
        var stabilizer = new DubStabilizer();

        // act
        var chunk = stabilizer.Next(Unstable("Hello wor"));

        // assert
        chunk.Should().BeNull("unstable text is never spoken");
        stabilizer.SentText.Should().BeEmpty();
    }

    [Fact]
    public void NextShouldReturnOnlyTheNewStableSuffix()
    {
        // arrange
        var stabilizer = new DubStabilizer();

        // act, assert - one step at a time, each depends on what the previous one sent
        stabilizer.Next(Stable("Hello world.")).Should().Be("Hello world.");
        stabilizer.Next(Stable("Hello world.")).Should().BeNull("nothing new is stable");
        stabilizer.Next(Unstable("Hello world. How are")).Should().BeNull();
        stabilizer.Next(Stable("Hello world. How are you?")).Should().Be(" How are you?");
        stabilizer.SentText.Should().Be("Hello world. How are you?");
    }

    [Fact]
    public void NextShouldResendFromTheDivergencePoint()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world."));

        // act
        var chunk = stabilizer.Next(Stable("Hello there, world."));

        // assert
        chunk.Should().Be("there, world.",
            "TTS can't retract, so the divergent tail is spoken again rather than lost");
        stabilizer.SentText.Should().Be("Hello there, world.");
    }

    [Fact]
    public void NextShouldIgnoreWhitespaceOnlyGrowth()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello."));

        // act
        var chunk = stabilizer.Next(Stable("Hello. "));

        // assert
        chunk.Should().BeNull("there is nothing to say in whitespace");
    }

    [Fact]
    public void SkipShouldLeaveOnlyTheClausesPastTheBacklogToSpeak()
    {
        // arrange - one translated clause per time map point, 1 s per char
        var stabilizer = new DubStabilizer();
        var fold = WithClauseEnds("Hello there. How are you? Fine.", 12, 25, 31) with { IsStable = true };

        // act
        stabilizer.Skip(fold, 25);
        var chunk = stabilizer.Next(fold);

        // assert
        chunk.Should().Be(" Fine.", "a listener who joined late must not hear the backlog read out");
        stabilizer.SentText.Should().Be("Hello there. How are you? Fine.");
    }

    [Fact]
    public void SkipShouldSpeakTheClauseTheJoinPointFallsIn()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        var fold = WithClauseEnds("Hello there. How are you? Fine.", 12, 25, 31) with { IsStable = true };

        // act
        stabilizer.Skip(fold, 20);
        var chunk = stabilizer.Next(fold);

        // assert
        chunk.Should().Be(" How are you? Fine.", "the skip ends at the first clause boundary past the join point");
    }

    [Fact]
    public void SkipShouldNeverRewindWhatWasSpoken()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        var first = WithClauseEnds("Hello there. How are you?", 12, 25) with { IsStable = true };
        stabilizer.Skip(first, 12);
        stabilizer.Next(first).Should().Be(" How are you?");

        // act - the next transcript is skipped against the same backlog
        var second = WithClauseEnds("Hello there. How are you? Fine.", 12, 25, 31) with { IsStable = true };
        stabilizer.Skip(second, 12);
        var chunk = stabilizer.Next(second);

        // assert
        chunk.Should().Be(" Fine.", "the backlog ends before what was spoken already");
    }

    [Fact]
    public void SkipShouldCoverAnUnstableBacklog()
    {
        // arrange
        var stabilizer = new DubStabilizer();

        // act
        stabilizer.Skip(WithClauseEnds("Hello there.", 12), 12);
        var chunk = stabilizer.Next(WithClauseEnds("Hello there. How are you?", 12, 25) with { IsStable = true });

        // assert
        chunk.Should().Be(" How are you?");
    }

    [Fact]
    public void NextShouldSendStableTextWhateverItEndsWith()
    {
        // arrange
        var stabilizer = new DubStabilizer();

        // act
        var chunk = stabilizer.Next(Stable("Hello world. How are"));

        // assert
        chunk.Should().Be("Hello world. How are",
            "the translator hands over whole clauses, so a stable text is spoken as it is, however it ends");
        stabilizer.SentText.Should().Be("Hello world. How are");
    }

    [Fact]
    public void DecideShouldSayNoDubWhenTheSourceIsAlreadyInTheTargetLanguage()
    {
        // act
        var decision = DubStabilizer
            .Decide(Stable("Hello there, how are you?", Languages.English), Unstable(""), Language.Parse("en-GB"));

        // assert
        decision.Should().Be(DubDecision.NoDub);
    }

    [Fact]
    public void DecideShouldSayDubWhenTheSourceLanguageDiffers()
    {
        // act
        var decision = DubStabilizer
            .Decide(Stable("Привет, как у тебя дела?", Languages.Russian), Unstable(""), Languages.English);

        // assert
        decision.Should().Be(DubDecision.Dub);
    }

    [Fact]
    public void DecideShouldDubASourceThatMixesTheTargetWithAnotherLanguage()
    {
        // act - a Russian utterance with an English word in it, for an English listener
        var decision = DubStabilizer
            .Decide(Stable("Привет, how are you дела?", Languages.Russian, Languages.English),
                Unstable(""), Languages.English);

        // assert
        decision.Should().Be(DubDecision.Dub, "a needless dub is cheaper than losing the utterance to a wrong NoDub");
    }

    [Fact]
    public void DecideShouldWaitForEnoughTextBeforeTrustingTheLanguage()
    {
        // act
        var decision = DubStabilizer
            .Decide(Stable("Hi", Languages.English), Unstable(""), Languages.English);

        // assert
        decision.Should().Be(DubDecision.Undecided);
    }

    [Fact]
    public void DecideShouldSayNoDubWhenTheTranslationRepeatsTheSource()
    {
        // act
        var decision = DubStabilizer
            .Decide(Unstable("Hello there, how are you doing"), Stable("Hello there, how are you"), Languages.English);

        // assert
        decision.Should().Be(DubDecision.NoDub,
            "the translator hands the text back verbatim when no translation is needed");
    }

    [Fact]
    public void DecideShouldSayDubWhenTheTranslationDiffers()
    {
        // act
        var decision = DubStabilizer
            .Decide(Unstable("Привет, как у тебя сегодня дела"), Stable("Hello, how are you today"), Languages.English);

        // assert
        decision.Should().Be(DubDecision.Dub);
    }

    [Fact]
    public void DecideShouldAnswerOnTheSourceAloneWhenItNamesALanguage()
    {
        // act - the source-only form RunDub asks before any translated text exists
        var tagged = DubStabilizer
            .Decide(Unstable("Привет, как дела", Languages.Russian), Transcript.Empty, Languages.English);
        var untagged = DubStabilizer.Decide(Unstable("Привет, как дела"), Transcript.Empty, Languages.English);

        // assert
        tagged.Should().Be(DubDecision.Dub, "an unstable source is enough once it names a language");
        untagged.Should().Be(DubDecision.Undecided, "with no language the translated text has to tell");
    }

    [Fact]
    public void DecideShouldStayUndecidedWhileTheTranslationIsUnstableOrShort()
    {
        // act
        var unstable = DubStabilizer
            .Decide(Unstable("Привет, как дела"), Unstable("Hello, how are you"), Languages.English);
        var brief = DubStabilizer.Decide(Unstable("Привет"), Stable("Hi"), Languages.English);

        // assert
        unstable.Should().Be(DubDecision.Undecided, "an unstable translation may still change");
        brief.Should().Be(DubDecision.Undecided, "a couple of characters can't tell the languages apart");
    }

    private static Transcript Stable(string text, params Language[] languages)
        => Unstable(text, languages) with { IsStable = true };

    private static Transcript WithClauseEnds(string text, params int[] ends)
    {
        var map = LinearMap.Zero;
        foreach (var end in ends)
            map = map.Append(new Vector2(end, end));
        return new Transcript(text, map, []);
    }

    private static Transcript Unstable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), languages);
}
