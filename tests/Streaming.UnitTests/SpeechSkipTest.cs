using ActualChat.Transcription;
using ActualChat.Streaming;

namespace ActualChat.Streaming.UnitTests;

public sealed class SpeechSkipTest
{
    [Fact]
    public void ShouldSpeakAShortMessageInFull()
    {
        // arrange
        var text = "Two minutes and I am there.";

        // act
        var skip = AudioStreamingBackend.GetSkipLength(text, Languages.English);

        // assert
        skip.Should().Be(0, "a message shorter than the floor is heard from its first word");
    }

    [Fact]
    public void ShouldLeaveTheFloorOfALongMessageToBeSpoken()
    {
        // arrange - well over the floor, so most of it is read rather than heard
        var text = string.Join(" ", Enumerable.Repeat("some words of an already written message", 40));

        // act
        var skip = AudioStreamingBackend.GetSkipLength(text, Languages.English);

        // assert
        skip.Should().BeGreaterThan(0);
        var spoken = text.Length - skip;
        var floor = SpeechRate.ToCharCount(Languages.English, Constants.Audio.MinSpokenTailDuration);
        spoken.Should().BeLessThanOrEqualTo(floor,
            "the joiner starts near the live edge, not at the beginning");
        spoken.Should().BeGreaterThan(floor / 2, "but still hears the floor's worth of it");
        text[skip - 1].Should().Be(' ', "speech starts at a word, not half way through one");
    }

    [Fact]
    public void ShouldMeasureTheFloorInTheLanguageBeingSpoken()
    {
        // A character carries far more sound in a dense script, so the same floor is far fewer
        // characters - measuring it in English would cut a Mandarin message to a fraction.

        // arrange
        var text = new string('x', 400);

        // act
        var english = AudioStreamingBackend.GetSkipLength(text, Languages.English);
        var mandarin = AudioStreamingBackend.GetSkipLength(text, Languages.Chinese);

        // assert
        mandarin.Should().BeGreaterThan(english, "fewer characters make up the same seconds");
    }
}
