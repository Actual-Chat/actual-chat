using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class DubStabilizerTest
{
    [Fact]
    public void NextShouldSkipUnstableTranscripts()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Unstable("Hello wor")).Should().BeNull();
        stabilizer.SentText.Should().BeEmpty();
    }

    [Fact]
    public void NextShouldReturnOnlyTheNewStableSuffix()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Stable("Hello world.")).Should().Be("Hello world.");
        stabilizer.Next(Stable("Hello world.")).Should().BeNull("nothing new is stable");
        stabilizer.Next(Unstable("Hello world. How are")).Should().BeNull();
        stabilizer.Next(Stable("Hello world. How are you?")).Should().Be(" How are you?");
        stabilizer.SentText.Should().Be("Hello world. How are you?");
    }

    [Fact]
    public void NextShouldResendFromTheDivergencePoint()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world."));

        var chunk = stabilizer.Next(Stable("Hello there, world."));

        chunk.Should().Be("there, world.",
            "TTS can't retract, so the divergent tail is spoken again rather than lost");
        stabilizer.SentText.Should().Be("Hello there, world.");
    }

    [Fact]
    public void NextShouldIgnoreWhitespaceOnlyGrowth()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello."));

        stabilizer.Next(Stable("Hello. ")).Should().BeNull();
    }

    [Fact]
    public void SkipShouldLeaveOnlyTheTextAfterItToSpeak()
    {
        // arrange
        var stabilizer = new DubStabilizer();

        // act
        stabilizer.Skip(Unstable("Hello there"));
        var chunk = stabilizer.Next(Stable("Hello there, how are you?"));

        // assert
        chunk.Should().Be(", how are you?", "a listener who joined late must not hear the backlog read out");
        stabilizer.SentText.Should().Be("Hello there, how are you?");
    }

    [Fact]
    public void NextShouldHoldAStableFragmentUntilAClauseBoundary()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Stable("Hello there my")).Should().BeNull(
            "Soniox holds a mid-clause fragment until more text arrives, so sending it gains nothing");
        stabilizer.SentText.Should().BeEmpty();
        stabilizer.Next(Stable("Hello there my friend, how")).Should().Be("Hello there my friend,");
        stabilizer.SentText.Should().Be("Hello there my friend,");
        stabilizer.Next(Stable("Hello there my friend, how are you?")).Should().Be(" how are you?");
    }

    [Theory]
    [InlineData("Hello. How", "Hello.")]
    [InlineData("Hello! How", "Hello!")]
    [InlineData("Hello? How", "Hello?")]
    [InlineData("Hello… How", "Hello…")]
    [InlineData("Hello; how", "Hello;")]
    [InlineData("Hello: how", "Hello:")]
    [InlineData("Hello, how", "Hello,")]
    [InlineData("你好。你", "你好。")]
    [InlineData("你好！你", "你好！")]
    [InlineData("你好？你", "你好？")]
    [InlineData("你好，你", "你好，")]
    [InlineData("你好；你", "你好；")]
    [InlineData("你好：你", "你好：")]
    public void NextShouldCutAtEveryClauseBoundary(string text, string expectedChunk)
        => new DubStabilizer().Next(Stable(text)).Should().Be(expectedChunk);

    [Fact]
    public void NextShouldNotTreatPunctuationInsideAWordAsABoundary()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Stable("It costs 3.5 dollars")).Should().BeNull("a decimal point ends no clause");
        stabilizer.Next(Stable("It costs 3.5 dollars, or")).Should().Be("It costs 3.5 dollars,");
    }

    [Fact]
    public void NextShouldNotResendForABoundaryInsideTheSentPrefix()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world."));

        stabilizer.Next(Stable("Hello world. How are")).Should().BeNull();
        stabilizer.SentText.Should().Be("Hello world.");
    }

    [Fact]
    public void NextShouldSendALongUnpunctuatedRunAnyway()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        var words = string.Join(' ', Enumerable.Repeat("word", 20));
        var longRun = string.Join(' ', Enumerable.Repeat("word", 30));

        // act
        var held = stabilizer.Next(Stable(words));
        var sent = stabilizer.Next(Stable(longRun));

        // assert
        words.Length.Should().BeLessThan(DubStabilizer.MaxUnpunctuatedLength);
        longRun.Length.Should().BeGreaterThan(DubStabilizer.MaxUnpunctuatedLength);
        held.Should().BeNull();
        sent.Should().Be(longRun, "a fragment past the cap must not wait for punctuation that may never come");
    }

    [Fact]
    public void FlushShouldSendTheTailWhateverItEndsWith()
    {
        // arrange
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world. How are"));

        // act
        var tail = stabilizer.Flush();

        // assert
        tail.Should().Be(" How are", "once the translation is complete nothing more is coming");
        stabilizer.SentText.Should().Be("Hello world. How are");
        stabilizer.Flush().Should().BeNull("the tail is sent once");
    }

    [Fact]
    public void FlushShouldSendNothingBeforeAnyStableText()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Unstable("Hello there"));

        stabilizer.Flush().Should().BeNull("unstable text is never spoken, not even at the end");
    }

    [Fact]
    public void FlushShouldSendTheLastStableTailWhenTheTranslationEndsUnstable()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world. How are"));
        stabilizer.Next(Unstable("Hello world. How are you"));

        stabilizer.Flush().Should().Be(" How are", "the stable tail is spoken; the unstable growth is not");
    }

    [Fact]
    public void SkipThenNextShouldHoldTheFragmentAfterTheBacklog()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Skip(Unstable("Hello there,"));
        stabilizer.Next(Stable("Hello there, how are")).Should().BeNull("the fragment after the backlog waits too");
        stabilizer.Next(Stable("Hello there, how are you?")).Should().Be(" how are you?");
        stabilizer.SentText.Should().Be("Hello there, how are you?");
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
        DubStabilizer.Decide(Unstable("Привет, как дела"), Unstable("Hello, how are you"), Languages.English)
            .Should().Be(DubDecision.Undecided);
        DubStabilizer.Decide(Unstable("Привет"), Stable("Hi"), Languages.English)
            .Should().Be(DubDecision.Undecided);
    }

    private static Transcript Stable(string text, params Language[] languages)
        => Unstable(text, languages) with { IsStable = true };

    private static Transcript Unstable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), languages);
}
