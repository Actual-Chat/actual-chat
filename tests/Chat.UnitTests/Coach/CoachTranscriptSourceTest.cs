using ActualChat.Chat.Coach;
using ActualChat.Transcription;

namespace ActualChat.Chat.UnitTests.Coach;

public class CoachTranscriptSourceTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static Transcript Stable(string text) => new (text, LinearMap.Zero, []) { IsStable = true };
    private static Transcript Unstable(string text) => new (text, LinearMap.Zero, []);

    [Fact]
    public async Task StableTextsShouldSkipTheWordsTheRecognizerMayStillRewrite()
    {
        // arrange
        var transcripts = new[] {
            Unstable("So um"),
            Stable("So, um,"),
            Unstable("So, um, I want"),
            Unstable("So, um, I went to"),
            Stable("So, um, I went to the store."),
        };

        // act
        var texts = await CoachTranscriptSource.StableTexts(transcripts.ToAsyncEnumerable(), default).ToListAsync();

        // assert
        texts.Should().Equal("So, um,", "So, um, I went to the store.");
    }

    [Fact]
    public async Task StableTextsShouldEndWithTheLastTextEvenWhenItNeverSettled()
    {
        // arrange
        var transcripts = new[] { Stable("So, um,"), Unstable("So, um, I went") };

        // act
        var texts = await CoachTranscriptSource.StableTexts(transcripts.ToAsyncEnumerable(), default).ToListAsync();

        // assert
        texts.Should().Equal("So, um,", "So, um, I went");
    }
}
