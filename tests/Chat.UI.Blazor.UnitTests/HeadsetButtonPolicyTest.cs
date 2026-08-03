using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HeadsetButtonPolicyTest
{
    [Theory]
    [InlineData(HeadsetKey.Hook)]
    [InlineData(HeadsetKey.PlayPause)]
    public void StartsAReplyInsideTheWindow(HeadsetKey key)
        => HeadsetButtonPolicy
            .Decide(key, isDown: true, repeatCount: 0, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.StartReply);

    [Fact]
    public void StopsAHotReply()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: true, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.StopReply);

    [Fact]
    public void StopsAHotReplyEvenAfterTheWindowClosed()
    {
        // The window can expire mid-reply; the second press must still be able to close the mic.
        HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true,
                hasAnswerWindow: false, isReplyHot: true, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.StopReply);
    }

    [Fact]
    public void PassesThroughOutsideTheWindow()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true,
                hasAnswerWindow: false, isReplyHot: false, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PassesThroughWhenDisabled()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: false,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PassesThroughOnAnUnknownKey()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Unknown, true, 0, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void ActsOnExactlyOneEdge()
    {
        // Handling both edges of one press would open the mic and immediately close it:
        // by the time ACTION_UP arrives the reply is hot, so the policy would map it to StopReply.
        HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, isDown: false, 0, true,
                hasAnswerWindow: true, isReplyHot: true, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.PassThrough);
    }

    [Fact]
    public void IgnoresAutoRepeat()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, repeatCount: 1, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PracticeModeNeverTransmits()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, isDown: true, repeatCount: 0, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: true)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PracticeModeStillStopsAHotReply()
    {
        // A mic opened before the panel was entered must stay closable: refusing to close it is
        // the unsafe direction, and stopping a transmission can't break the "won't transmit" promise.
        HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, isDown: true, repeatCount: 0, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: true, isPracticeMode: true)
            .Should().Be(HeadsetButtonAction.StopReply);
    }
}
