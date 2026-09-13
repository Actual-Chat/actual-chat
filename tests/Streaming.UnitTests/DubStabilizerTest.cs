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
