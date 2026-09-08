using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureActivationPolicyTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<ChatId, Moment> NoVoice = new Dictionary<ChatId, Moment>();

    [Fact]
    public void PracticeModeSensesTheStopGestureRegardlessOfTheToggle()
    {
        // act + assert: the playground must let the user rehearse face-down/pocket even when
        // the privacy toggle is off and no mic is open.
        GestureActivationPolicy.ShouldSenseStopGesture(false, false, isPracticeMode: true).Should().BeTrue();
        GestureActivationPolicy.ShouldSenseStopGesture(true, true, isPracticeMode: true).Should().BeTrue();
    }

    [Fact]
    public void StopGestureNeedsTheToggleAndAnOpenMicOutsidePractice()
    {
        // act + assert
        GestureActivationPolicy.ShouldSenseStopGesture(true, true, isPracticeMode: false).Should().BeTrue();
        GestureActivationPolicy.ShouldSenseStopGesture(true, false, isPracticeMode: false).Should().BeFalse();
        GestureActivationPolicy.ShouldSenseStopGesture(false, true, isPracticeMode: false).Should().BeFalse();
    }

    [Fact]
    public void SensesInsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(20) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeTrue();
    }

    [Fact]
    public void DoesNotSenseOutsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(400) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void IgnoresVoiceInNonPttChats()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatB] = T0 - TimeSpan.FromSeconds(5) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void AlwaysOnSensesWithoutVoice()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [ChatA], NoVoice, T0, Window)
            .Should().BeTrue();

    [Fact]
    public void AlwaysOnStillNeedsAtLeastOnePttChat()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [], NoVoice, T0, Window)
            .Should().BeFalse();

    [Fact]
    public void PracticeModeSensesWithNoPttChatsAtAll()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(false, true, [], NoVoice, T0, Window)
            .Should().BeTrue();

    [Fact]
    public void OpeningTheAppShouldNotArmStartGestures()
        // Voice is the only thing that arms them: an armed chat plus an open app used to be
        // enough, which left a jostle-sized surface live for the whole answer window.
        => GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], NoVoice, T0, Window)
            .Should().BeFalse();

    [Fact]
    public void AnswerWindowChatIsTheMostRecentOne()
    {
        // arrange
        var last = new Dictionary<ChatId, Moment> {
            [ChatA] = T0 - TimeSpan.FromSeconds(100),
            [ChatB] = T0 - TimeSpan.FromSeconds(20),
        };

        // act
        var answer = GestureActivationPolicy.GetAnswerWindowChat([ChatA, ChatB], last, T0, Window);

        // assert
        answer.Should().NotBeNull();
        answer!.Value.ChatId.Should().Be(ChatB);
        answer.Value.At.Should().Be(T0 - TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void AnswerWindowChatIgnoresStaleAndNonPttStamps()
    {
        // arrange
        var last = new Dictionary<ChatId, Moment> {
            [ChatA] = T0 - TimeSpan.FromSeconds(400),
            [ChatB] = T0 - TimeSpan.FromSeconds(1),
        };

        // act
        var answer = GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window);

        // assert
        answer.Should().BeNull();
    }

    [Fact]
    public void ClearingTheStampClosesTheAnswerWindow()
    {
        // What ActivitiesBackend's Stop action does through VoiceActivityUI.ClearIncomingVoice:
        // without this the widget recomputes the identical state and the notification comes back.

        // arrange
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(5) };
        var beforeClear = GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window);

        // act
        last.Remove(ChatA);

        // assert
        beforeClear.Should().NotBeNull();
        GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window).Should().BeNull();
        GestureActivationPolicy.HasAnswerWindow([ChatA], last, T0, Window).Should().BeFalse();
    }

    [Theory]
    [InlineData(GestureKind.FlipToTalk, GestureRoute.StartReply)]
    [InlineData(GestureKind.DoubleShake, GestureRoute.StartReply)]
    [InlineData(GestureKind.None, GestureRoute.None)]
    public void RoutesGesturesOutsidePracticeMode(GestureKind kind, GestureRoute expected)
        => GestureActivationPolicy
            .Route(kind, false, isMicOpen: false, isStopArmed: false, isHushArmed: false)
            .Should().Be(expected);

    [Fact]
    public void ShakeWhileTheMicIsOpenShouldStop()
        // Nothing else can be meant by shaking a phone whose mic is already open - and it's the
        // one stop gesture that needs neither a surface nor a pocket.
        => GestureActivationPolicy
            .Route(GestureKind.DoubleShake, false, isMicOpen: true, isStopArmed: false, isHushArmed: false)
            .Should().Be(GestureRoute.StopReply);

    [Fact]
    public void FlipWhileTheMicIsOpenShouldStillRouteToStart()
        // Which RequestReply then no-ops on the already-hot mic: only the shake is deliberate
        // enough to be reused as a stop.
        => GestureActivationPolicy
            .Route(GestureKind.FlipToTalk, false, isMicOpen: true, isStopArmed: false, isHushArmed: false)
            .Should().Be(GestureRoute.StartReply);

    [Fact]
    public void PracticeModeNeverTransmits()
    {
        foreach (var kind in Enum.GetValues<GestureKind>())
        foreach (var isTransmitting in new[] { false, true }) {
            var route = GestureActivationPolicy.Route(kind, true, isTransmitting, false, isHushArmed: false);
            route.Should().NotBe(GestureRoute.StartReply, $"{kind} must not open the mic in practice mode");
            route.Should().NotBe(GestureRoute.StopReply, $"{kind} must not touch the mic in practice mode");
        }
    }

    [Fact]
    public void PracticeModeRoutesRealGesturesToThePanel()
    {
        GestureActivationPolicy.Route(GestureKind.FlipToTalk, true, false, false, false)
            .Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.DoubleShake, true, false, false, false)
            .Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.FaceDown, true, false, false, false)
            .Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.Pocket, true, false, false, false)
            .Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.None, true, false, false, false).Should().Be(GestureRoute.None);
    }

    [Fact]
    public void ShakeShouldBeSensedWithAnOpenMicAndShakeToTalkOff()
        // The stop side rides the stop toggle: turning off a way to open the mic must never take
        // away a way to close it.
        => GestureActivationPolicy
            .ShouldSenseShake(
                isDoubleShakeEnabled: false, mustSenseStart: false,
                mustSenseStop: true, isMicOpen: true)
            .Should().BeTrue();

    [Fact]
    public void ShakeShouldNotBeSensedForAVideoOnlyStream()
        // mustSenseStop also covers an outgoing camera or screencast, where a sensed shake would
        // route to StartReply and open the very mic it isn't there to close.
        => GestureActivationPolicy
            .ShouldSenseShake(
                isDoubleShakeEnabled: false, mustSenseStart: false,
                mustSenseStop: true, isMicOpen: false)
            .Should().BeFalse();

    [Fact]
    public void ShakeShouldNotBeSensedWhenNeitherSideWantsIt()
    {
        // act + assert
        GestureActivationPolicy
            .ShouldSenseShake(
                isDoubleShakeEnabled: true, mustSenseStart: false,
                mustSenseStop: false, isMicOpen: false)
            .Should().BeFalse("nothing is armed and no mic is open");
        GestureActivationPolicy
            .ShouldSenseShake(
                isDoubleShakeEnabled: false, mustSenseStart: true,
                mustSenseStop: false, isMicOpen: false)
            .Should().BeFalse("shake-to-talk is off");
    }

    [Fact]
    public void HushSensesWhileVoiceIsLiveOrTheWindowIsOpen()
    {
        // act + assert
        GestureActivationPolicy
            .ShouldSenseHush(true, false, hasArmedChats: true, hasLiveIncoming: true, hasAnswerWindow: false)
            .Should().BeTrue();
        GestureActivationPolicy
            .ShouldSenseHush(true, false, hasArmedChats: true, hasLiveIncoming: false, hasAnswerWindow: true)
            .Should().BeTrue();
        GestureActivationPolicy
            .ShouldSenseHush(true, false, hasArmedChats: true, hasLiveIncoming: false, hasAnswerWindow: false)
            .Should().BeFalse();
    }

    [Fact]
    public void HushNeedsTheToggleAndAnArmedChatAndNeverArmsInPractice()
    {
        // act + assert
        GestureActivationPolicy.ShouldSenseHush(false, false, true, true, true).Should().BeFalse();
        GestureActivationPolicy.ShouldSenseHush(true, false, false, true, true).Should().BeFalse();
        GestureActivationPolicy.ShouldSenseHush(true, true, true, true, true)
            .Should().BeFalse("practice rehearses the detectors, hush arming is a live-only decision");
    }

    [Fact]
    public void FaceDownRoutesToStopReplyWhileStopSensingIsArmedElseToHush()
    {
        // act + assert
        GestureActivationPolicy
            .Route(GestureKind.FaceDown, false, isMicOpen: true, isStopArmed: true, isHushArmed: true)
            .Should().Be(GestureRoute.StopReply);
        // Video-only stream: mic closed, but stop sensing is still armed by the camera/screencast.
        GestureActivationPolicy
            .Route(GestureKind.FaceDown, false, isMicOpen: false, isStopArmed: true, isHushArmed: true)
            .Should().Be(GestureRoute.StopReply);
        GestureActivationPolicy
            .Route(GestureKind.FaceDown, false, isMicOpen: false, isStopArmed: false, isHushArmed: true)
            .Should().Be(GestureRoute.Hush);
        GestureActivationPolicy
            .Route(GestureKind.FaceDown, false, isMicOpen: false, isStopArmed: false, isHushArmed: false)
            .Should().Be(GestureRoute.None);
    }

    [Fact]
    public void FaceDownNeverHushesOverAnOpenMic()
    {
        // Stop sensing off (privacy toggle) + mic open + hush armed: muting every chat while the mic
        // keeps recording into one of them would be the worst of both, so nothing fires.
        GestureActivationPolicy
            .Route(GestureKind.FaceDown, false, isMicOpen: true, isStopArmed: false, isHushArmed: true)
            .Should().Be(GestureRoute.None);
    }

    [Fact]
    public void PocketNeverHushesAndPatOnlyHushes()
    {
        // act + assert
        GestureActivationPolicy
            .Route(GestureKind.Pocket, false, isMicOpen: false, isStopArmed: false, isHushArmed: true)
            .Should().Be(GestureRoute.None, "a pocketed phone hushes by pat, never by being pocketed");
        GestureActivationPolicy
            .Route(GestureKind.Pocket, false, isMicOpen: false, isStopArmed: true, isHushArmed: true)
            .Should().Be(GestureRoute.StopReply);
        GestureActivationPolicy
            .Route(GestureKind.DoublePat, false, isMicOpen: false, isStopArmed: false, isHushArmed: true)
            .Should().Be(GestureRoute.Hush);
        GestureActivationPolicy
            .Route(GestureKind.DoublePat, false, isMicOpen: true, isStopArmed: false, isHushArmed: true)
            .Should().Be(GestureRoute.None);
        GestureActivationPolicy
            .Route(GestureKind.DoublePat, true, isMicOpen: false, isStopArmed: false, isHushArmed: false)
            .Should().Be(GestureRoute.Practice);
    }
}

public class StartGestureReadinessTest
{
    [Fact]
    public void SensedAndEnabledGestureShouldBeReady()
        => GestureActivationPolicy.IsStartGestureReady(
                mustSenseStartGestures: true, isFlipToTalkEnabled: true,
                isDoubleShakeEnabled: false, isPracticeMode: false)
            .Should().BeTrue();

    [Fact]
    public void UnsensedGestureShouldNotBeReady()
        => GestureActivationPolicy.IsStartGestureReady(
                mustSenseStartGestures: false, isFlipToTalkEnabled: true,
                isDoubleShakeEnabled: true, isPracticeMode: false)
            .Should().BeFalse("the accelerometer is stopped outside the arming window");

    [Fact]
    public void BothGestureTogglesOffShouldNotBeReady()
        => GestureActivationPolicy.IsStartGestureReady(
                mustSenseStartGestures: true, isFlipToTalkEnabled: false,
                isDoubleShakeEnabled: false, isPracticeMode: false)
            .Should().BeFalse("no start gesture exists to fire");

    [Fact]
    public void PracticeModeShouldNotBeReady()
        => GestureActivationPolicy.IsStartGestureReady(
                mustSenseStartGestures: true, isFlipToTalkEnabled: true,
                isDoubleShakeEnabled: true, isPracticeMode: true)
            .Should().BeFalse("practice mode never transmits");

    [Fact]
    public void DoubleShakeAloneShouldBeReady()
        => GestureActivationPolicy.IsStartGestureReady(
                mustSenseStartGestures: true, isFlipToTalkEnabled: false,
                isDoubleShakeEnabled: true, isPracticeMode: false)
            .Should().BeTrue();
}
