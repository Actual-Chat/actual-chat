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

    [Fact]
    public void AcceptsSameScriptRefine()
    {
        const string realtimeText = "hello world this is a test message";
        const string refinedText = "Hello, world! This is a test message.";
        realtimeText.ShouldUseOriginalTranscript(refinedText).Should().BeFalse();
    }
}
