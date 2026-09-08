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
        => GetState(new UserPttSettings(), RecentVoiceInA).HasAnswerWindow.Should().BeTrue();

    [Fact]
    public void StaleVoiceLeavesTheWindowClosed()
        => GetState(new UserPttSettings(), OldVoiceInA).HasAnswerWindow.Should().BeFalse();

    [Fact]
    public void VoiceInAnUnarmedChatLeavesTheWindowClosed()
        => GetState(new UserPttSettings(), RecentVoiceInB).HasAnswerWindow.Should().BeFalse();

    [Fact]
    public void AnEmptyArmedSetLeavesTheWindowClosed()
    {
        // act
        var state = GetState(new UserPttSettings(), RecentVoiceInA, pttChatIds: []);

        // assert
        state.HasAnswerWindow.Should().BeFalse();
        state.HasArmedChats.Should().BeFalse();
    }

    [Fact]
    public void ArmedChatsAreReportedWhenTheSetIsNonEmpty()
        => GetState(new UserPttSettings(), RecentVoiceInA).HasArmedChats.Should().BeTrue();

    [Fact]
    public void AlwaysOnGesturesNeverOpenTheWindow()
    {
        // arrange
        var settings = new UserPttSettings { AreGesturesAlwaysOn = true };

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
        var state = GetState(new UserPttSettings(), NoVoice, isPracticeMode: true);

        // assert
        state.HasAnswerWindow.Should().BeFalse();
        state.IsPracticeMode.Should().BeTrue();
    }

    [Fact]
    public void AlwaysOnPlusPracticeModeStillDoesNotStartAReply()
    {
        // arrange
        var settings = new UserPttSettings { AreGesturesAlwaysOn = true };

        // act
        var state = GetState(settings, NoVoice, isPracticeMode: true);

        // assert
        Decide(state).Should().Be(HeadsetButtonAction.PassThrough);
    }

    [Fact]
    public void AMissingSettingReadsAsEnabled()
        => GetState(new UserPttSettings { IsHeadsetButtonEnabled = null }, RecentVoiceInA)
            .IsEnabled.Should().BeTrue();

    [Fact]
    public void AnExplicitlyDisabledSettingReadsAsDisabled()
        => GetState(new UserPttSettings { IsHeadsetButtonEnabled = false }, RecentVoiceInA)
            .IsEnabled.Should().BeFalse();

    [Fact]
    public void AHotReplyIsCarriedThroughToTheDecision()
    {
        // act
        var state = GetState(new UserPttSettings(), NoVoice, isReplyHot: true);

        // assert
        Decide(state).Should().Be(HeadsetButtonAction.StopReply);
    }

    [Theory]
    [InlineData(HeadsetKey.Hook)]
    [InlineData(HeadsetKey.PlayPause)]
    public void AShortPressStartsAReplyOnReleaseInsideTheWindow(HeadsetKey key)
    {
        // act + assert: the first edge is swallowed, the release acts
        Down(key, hasAnswerWindow: true).Should().Be(HeadsetButtonAction.Consume);
        Up(key, hasAnswerWindow: true).Should().Be(HeadsetButtonAction.StartReply);
    }

    [Fact]
    public void AShortPressStopsAHotReplyOnRelease()
    {
        // act + assert
        Down(HeadsetKey.Hook, hasAnswerWindow: true, isReplyHot: true).Should().Be(HeadsetButtonAction.Consume);
        Up(HeadsetKey.Hook, hasAnswerWindow: true, isReplyHot: true).Should().Be(HeadsetButtonAction.StopReply);
    }

    [Fact]
    public void StopsAHotReplyEvenAfterTheWindowClosed()
        // The window can expire mid-reply; the release must still be able to close the mic.
        => Up(HeadsetKey.Hook, hasAnswerWindow: false, isReplyHot: true).Should().Be(HeadsetButtonAction.StopReply);

    [Fact]
    public void PassesThroughWithNothingToDo()
    {
        // No reply possible and nothing armed: the system keeps its play/pause handling.
        Down(HeadsetKey.Hook, hasAnswerWindow: false, hasArmedChats: false)
            .Should().Be(HeadsetButtonAction.PassThrough);
        Up(HeadsetKey.Hook, hasAnswerWindow: false, hasArmedChats: false)
            .Should().Be(HeadsetButtonAction.PassThrough);
    }

    [Fact]
    public void AnOwnedPressOutsideTheWindowIsSwallowedWhole()
    {
        // An armed chat makes the press ours (it might turn out long), so its release must not
        // fall through to the system as half a click.
        Down(HeadsetKey.Hook, hasAnswerWindow: false).Should().Be(HeadsetButtonAction.Consume);
        Up(HeadsetKey.Hook, hasAnswerWindow: false).Should().Be(HeadsetButtonAction.Consume);
    }

    [Fact]
    public void PassesThroughWhenDisabled()
        => Up(HeadsetKey.Hook, hasAnswerWindow: true, isEnabled: false).Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PassesThroughOnAnUnknownKey()
        => Up(HeadsetKey.Unknown, hasAnswerWindow: true).Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void AutoRepeatsAreSwallowed()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, isDown: true, isLongPress: false, wasLongPressHandled: false, isEnabled: true,
                hasAnswerWindow: true, isReplyHot: false, isPracticeMode: false, hasArmedChats: true)
            .Should().Be(HeadsetButtonAction.Consume);

    [Fact]
    public void PracticeModeNeverTransmits()
        // Nothing to reply to and nothing to hush from the practice panel: the system keeps the press.
        => Up(HeadsetKey.Hook, hasAnswerWindow: true, isPracticeMode: true)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PracticeModeStillStopsAHotReply()
        // A mic opened before the panel was entered must stay closable: refusing to close it is
        // the unsafe direction, and stopping a transmission can't break the "won't transmit" promise.
        => Up(HeadsetKey.Hook, hasAnswerWindow: true, isReplyHot: true, isPracticeMode: true)
            .Should().Be(HeadsetButtonAction.StopReply);

    [Fact]
    public void ALongPressHushesAndItsReleaseIsSwallowed()
    {
        // act + assert: the mic never opened on the way, and the release doesn't start a reply
        Down(HeadsetKey.Hook, hasAnswerWindow: true).Should().Be(HeadsetButtonAction.Consume);
        Down(HeadsetKey.Hook, hasAnswerWindow: true, isLongPress: true).Should().Be(HeadsetButtonAction.Hush);
        Up(HeadsetKey.Hook, hasAnswerWindow: true, wasLongPressHandled: true).Should().Be(HeadsetButtonAction.Consume);
    }

    [Fact]
    public void ALongPressDoesNotHushWithNothingArmedOrInPractice()
    {
        // act + assert
        Down(HeadsetKey.Hook, hasAnswerWindow: false, isLongPress: true, hasArmedChats: false)
            .Should().Be(HeadsetButtonAction.PassThrough, "nothing armed and no reply possible");
        Down(HeadsetKey.Hook, hasAnswerWindow: true, isLongPress: true, isPracticeMode: true)
            .Should().Be(HeadsetButtonAction.PassThrough, "practice neither replies nor hushes");
        Down(HeadsetKey.Hook, hasAnswerWindow: true, isLongPress: true, isReplyHot: true, hasArmedChats: false)
            .Should().Be(HeadsetButtonAction.Consume, "a hot reply with nothing armed closes on release, not by hush");
    }

    // Private methods

    private static HeadsetButtonState GetState(
        UserPttSettings settings,
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
        // The release is the edge a short press acts on.
        => HeadsetButtonPolicy.Decide(
            HeadsetKey.PlayPause,
            isDown: false,
            isLongPress: false,
            wasLongPressHandled: false,
            state.IsEnabled,
            state.HasAnswerWindow,
            state.IsReplyHot,
            state.IsPracticeMode,
            state.HasArmedChats);

    private static HeadsetButtonAction Down(
        HeadsetKey key,
        bool hasAnswerWindow,
        bool isLongPress = false,
        bool isEnabled = true,
        bool isReplyHot = false,
        bool isPracticeMode = false,
        bool hasArmedChats = true)
        => HeadsetButtonPolicy.Decide(key, true, isLongPress, false,
            isEnabled, hasAnswerWindow, isReplyHot, isPracticeMode, hasArmedChats);

    private static HeadsetButtonAction Up(
        HeadsetKey key,
        bool hasAnswerWindow,
        bool wasLongPressHandled = false,
        bool isEnabled = true,
        bool isReplyHot = false,
        bool isPracticeMode = false,
        bool hasArmedChats = true)
        => HeadsetButtonPolicy.Decide(key, false, false, wasLongPressHandled,
            isEnabled, hasAnswerWindow, isReplyHot, isPracticeMode, hasArmedChats);
}
