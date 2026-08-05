using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HeadsetButtonPolicyTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<ChatId, Moment> NoVoice = new Dictionary<ChatId, Moment>();
    private static readonly IReadOnlyDictionary<ChatId, Moment> RecentVoiceInA =
        new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(20) };
    private static readonly IReadOnlyDictionary<ChatId, Moment> OldVoiceInA =
        new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(400) };
    private static readonly IReadOnlyDictionary<ChatId, Moment> RecentVoiceInB =
        new Dictionary<ChatId, Moment> { [ChatB] = T0 - TimeSpan.FromSeconds(20) };

    [Fact]
    public void RecentVoiceInAnArmedChatOpensTheWindow()
        => GetState(new UserWalkieTalkieSettings(), RecentVoiceInA).HasAnswerWindow.Should().BeTrue();

    [Fact]
    public void StaleVoiceLeavesTheWindowClosed()
        => GetState(new UserWalkieTalkieSettings(), OldVoiceInA).HasAnswerWindow.Should().BeFalse();

    [Fact]
    public void VoiceInAnUnarmedChatLeavesTheWindowClosed()
        => GetState(new UserWalkieTalkieSettings(), RecentVoiceInB).HasAnswerWindow.Should().BeFalse();

    [Fact]
    public void AnEmptyArmedSetLeavesTheWindowClosed()
        => GetState(new UserWalkieTalkieSettings(), RecentVoiceInA, pttChatIds: []).HasAnswerWindow.Should().BeFalse();

    [Fact]
    public void AlwaysOnGesturesNeverOpenTheWindow()
    {
        // arrange
        var settings = new UserWalkieTalkieSettings { AreGesturesAlwaysOn = true };

        // act
        var withNoVoice = GetState(settings, NoVoice);
        var withOldVoice = GetState(settings, OldVoiceInA);
        var mustSenseGestures = GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [ChatA], NoVoice, T0, Window);

        // assert
        withNoVoice.HasAnswerWindow.Should().BeFalse();
        withOldVoice.HasAnswerWindow.Should().BeFalse();
        mustSenseGestures.Should().BeTrue("the gesture consumer keeps its always-on behavior");
    }

    [Fact]
    public void PracticeModeNeverOpensTheWindow()
    {
        // act
        var state = GetState(new UserWalkieTalkieSettings(), NoVoice, isPracticeMode: true);

        // assert
        state.HasAnswerWindow.Should().BeFalse();
        state.IsPracticeMode.Should().BeTrue();
    }

    [Fact]
    public void AlwaysOnPlusPracticeModeStillDoesNotStartAReply()
    {
        // arrange
        var settings = new UserWalkieTalkieSettings { AreGesturesAlwaysOn = true };

        // act
        var state = GetState(settings, NoVoice, isPracticeMode: true);

        // assert
        Decide(state).Should().Be(HeadsetButtonAction.PassThrough);
    }

    [Fact]
    public void AMissingSettingReadsAsEnabled()
        => GetState(new UserWalkieTalkieSettings { IsHeadsetButtonEnabled = null }, RecentVoiceInA)
            .IsEnabled.Should().BeTrue();

    [Fact]
    public void AnExplicitlyDisabledSettingReadsAsDisabled()
        => GetState(new UserWalkieTalkieSettings { IsHeadsetButtonEnabled = false }, RecentVoiceInA)
            .IsEnabled.Should().BeFalse();

    [Fact]
    public void AHotReplyIsCarriedThroughToTheDecision()
    {
        // act
        var state = GetState(new UserWalkieTalkieSettings(), NoVoice, isReplyHot: true);

        // assert
        Decide(state).Should().Be(HeadsetButtonAction.StopReply);
    }

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

    // Private methods

    private static HeadsetButtonState GetState(
        UserWalkieTalkieSettings settings,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        IReadOnlyList<ChatId>? pttChatIds = null,
        bool isReplyHot = false,
        bool isPracticeMode = false)
        => HeadsetButtonPolicy.GetState(
            settings,
            pttChatIds ?? [ChatA],
            lastIncomingVoiceAt,
            T0,
            Window,
            isReplyHot,
            isPracticeMode);

    private static HeadsetButtonAction Decide(HeadsetButtonState state)
        => HeadsetButtonPolicy.Decide(
            HeadsetKey.PlayPause,
            isDown: true,
            repeatCount: 0,
            state.IsEnabled,
            state.HasAnswerWindow,
            state.IsReplyHot,
            state.IsPracticeMode);
}
