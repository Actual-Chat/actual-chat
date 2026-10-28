using ActualChat.Streaming;

namespace ActualChat.Transcription.UnitTests;

public class TextScriptTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("Привет, как дела", nameof(TextScript.Cyrillic))]
    [InlineData("Hello, how are you", nameof(TextScript.Latin))]
    [InlineData("Tam niech zawsze wyrażne", nameof(TextScript.Latin))]
    [InlineData("Xin chào các bạn", nameof(TextScript.Latin))]
    [InlineData("Οντάξει, συγγνώμη", nameof(TextScript.Greek))]
    [InlineData("こんにちは", nameof(TextScript.Cjk))]
    [InlineData("日本語のテスト", nameof(TextScript.Cjk))]
    [InlineData("你好世界", nameof(TextScript.Cjk))]
    [InlineData("안녕하세요", nameof(TextScript.Hangul))]
    [InlineData("สวัสดีครับ", nameof(TextScript.Thai))]
    [InlineData("नमस्ते दुनिया", nameof(TextScript.Devanagari))]
    [InlineData("السلام علیکم", nameof(TextScript.Arabic))]
    [InlineData("வணக்கம் உலகம்", nameof(TextScript.Tamil))]
    [InlineData("12345 !?.,", nameof(TextScript.None))]
    [InlineData("", nameof(TextScript.None))]
    public void DetectsDominantScript(string text, string expected)
        => text.GetDominantScript().ToString().Should().Be(expected);

    [Theory]
    // Wrong-script refine hallucinations are rejected (true = keep real-time).
    [InlineData("Да я", "Οντάξει.")]
    [InlineData("Он там разные.", "Tam niech zawsze wyrażne.")]
    [InlineData("안녕하세요", "こんにちは、元気ですか")]
    public void RejectsRefineWithDifferentScript(string realtimeText, string refinedText)
        => realtimeText.ShouldUseOriginalTranscript(refinedText).Should().BeTrue();

    [Theory]
    [InlineData("да ну давай", "Да, давай.", false)]
    [InlineData("привет как дела у тебя", "Привет.", true)]
    [InlineData("щас", "Сейчас.", false)]
    [InlineData("ну в общем я думаю что надо это сделать до конца недели",
        "Ну, в общем, надо до конца недели.", true)]
    [InlineData("ну в общем я думаю что надо это сделать до конца недели",
        "Ну, в общем, я думаю, надо это сделать до конца недели.", false)]
    public void RefineShouldWinUnlessMuchShorter(string realtimeText, string refinedText, bool mustUseOriginal)
        // Under 50 chars the refined text must keep 80% of the length, from 50 chars on 90%
        => realtimeText.ShouldUseOriginalTranscript(refinedText).Should().Be(mustUseOriginal);

    [Theory]
    [InlineData("Да я", "Okay, yeah.", false)]
    [InlineData("привет как дела у тебя", "Привет, как ты?", false)]
    [InlineData("привет как дела у тебя", "Привет.", true)]
    [InlineData("ну в общем я думаю что надо это сделать до конца недели",
        "Ну, в общем, надо это сделать до конца недели.", false)]
    [InlineData("ну в общем я думаю что надо это сделать до конца недели", "Ну, до конца недели.", true)]
    [InlineData("что-то", "", true)]
    public void SonioxRefineShouldWinUnlessEmptyOrHalved(string realtimeText, string refinedText, bool mustUseOriginal)
        // Soniox skips the script check, and its bars are 60% under 50 chars and 80% from 50 chars on
        => realtimeText.ShouldUseOriginalTranscript(refinedText, TranscriberId.SonioxOffline)
            .Should().Be(mustUseOriginal);

    [Fact]
    public void AcceptsSameScriptRefine()
    {
        const string realtimeText = "hello world this is a test message";
        const string refinedText = "Hello, world! This is a test message.";
        realtimeText.ShouldUseOriginalTranscript(refinedText).Should().BeFalse();
    }
}
