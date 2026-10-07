using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallScreensUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);

    [Theory]
    [InlineData(CallRole.Callee, CallPhase.Ringing, false, CallViewKind.Modal)]
    [InlineData(CallRole.Callee, CallPhase.Ringing, true, CallViewKind.Modal)]
    [InlineData(CallRole.Caller, CallPhase.Dialing, false, CallViewKind.Modal)]
    [InlineData(CallRole.Caller, CallPhase.Dialing, true, CallViewKind.FullScreen)]
    [InlineData(CallRole.Callee, CallPhase.Active, false, CallViewKind.None)]
    [InlineData(CallRole.Callee, CallPhase.Active, true, CallViewKind.FullScreen)]
    [InlineData(CallRole.Caller, CallPhase.Active, false, CallViewKind.None)]
    [InlineData(CallRole.Caller, CallPhase.Active, true, CallViewKind.FullScreen)]
    public void ViewShouldFollowPhaseAndWidth(CallRole origin, CallPhase phase, bool isNarrow, CallViewKind expected)
    {
        // arrange
        var call = Call(origin, phase);

        // act
        var view = Decide(call, isNarrow);

        // assert
        view.Kind.Should().Be(expected);
        view.Call.Should().Be(call, "the view loop tells a released slot by the call going away, even from None");
        view.IsOverLock.Should().BeFalse();
    }

    [Fact]
    public void FreeSlotShouldShowNothing()
    {
        // act
        var view = Decide(null, isNarrow: true);

        // assert
        view.Should().Be(CallView.None);
    }

    [Fact]
    public void DialingShouldShowOnTheGestureAlone()
    {
        // arrange - the slot is claimed before the StartCall RPC, and the screens follow it
        var call = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var view = Decide(call, isNarrow: true);

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen, "the invitee is known from the click, so nothing is waited for");
        view.Call.Should().Be(call, "the view loop tells a released slot by the call going away");
    }

    [Theory]
    [InlineData(CallRole.Callee, CallPhase.Ringing, false)]
    [InlineData(CallRole.Callee, CallPhase.Ringing, true)]
    [InlineData(CallRole.Caller, CallPhase.Dialing, false)]
    [InlineData(CallRole.Caller, CallPhase.Dialing, true)]
    public void CollapsedCallShouldShowIsland(CallRole origin, CallPhase phase, bool isNarrow)
    {
        // act
        var view = Decide(Call(origin, phase), isNarrow, Flags(collapsed: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.Collapsed);
    }

    [Theory]
    [InlineData(CallRole.Caller, false, CallViewKind.None)]
    [InlineData(CallRole.Caller, true, CallViewKind.Collapsed)]
    [InlineData(CallRole.Callee, false, CallViewKind.None)]
    [InlineData(CallRole.Callee, true, CallViewKind.Collapsed)]
    public void CollapsedActiveCallShouldShowIslandOnlyOnNarrowScreen(
        CallRole origin, bool isNarrow, CallViewKind expected)
    {
        // act
        var view = Decide(Call(origin, CallPhase.Active), isNarrow, Flags(collapsed: ChatA));

        // assert
        view.Kind.Should().Be(expected, "the island leads back to a full-screen view only a narrow screen has");
    }

    [Theory]
    [InlineData(CallPhase.Ringing)]
    [InlineData(CallPhase.Active)]
    public void OverLockShouldShowFullScreenOnAnyWidth(CallPhase phase)
    {
        // act
        var view = Decide(Call(CallRole.Callee, phase), isNarrow: false, Flags(overLock: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
        view.IsOverLock.Should().BeTrue();
    }

    [Theory]
    [InlineData(CallPhase.Ringing)]
    [InlineData(CallPhase.Active)]
    public void OverLockShouldOverrideCollapse(CallPhase phase)
    {
        // arrange
        var flags = Flags(collapsed: ChatA, overLock: ChatA);

        // act
        var view = Decide(Call(CallRole.Callee, phase), isNarrow: true, flags);

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
        view.IsOverLock.Should().BeTrue();
    }

    [Fact]
    public void OverLockShouldBeIgnoredForOutgoingCall()
    {
        // act
        var view = Decide(Call(CallRole.Caller, CallPhase.Dialing), isNarrow: false, Flags(overLock: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.Modal);
        view.IsOverLock.Should().BeFalse();
    }

    [Fact]
    public void FlagsOfAnotherChatShouldBeIgnored()
    {
        // arrange
        var flags = Flags(collapsed: ChatB, overLock: ChatB);

        // act
        var ringView = Decide(Call(CallRole.Callee, CallPhase.Ringing), isNarrow: false, flags);
        var activeView = Decide(Call(CallRole.Callee, CallPhase.Active), isNarrow: true, flags);

        // assert
        ringView.Kind.Should().Be(CallViewKind.Modal);
        activeView.Kind.Should().Be(CallViewKind.FullScreen);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void OverLockFlagShouldGoStaleOnceRingOutlived(bool isFlagSet, bool isSameRing, bool isHeld, bool isStale)
    {
        // act
        var isFlagStale = CallScreensUI.IsOverLockFlagStale(
            isFlagSet ? ChatA : null, ChatA, isSameRing, isHeld ? ChatA : null);

        // assert
        isFlagStale.Should().Be(isStale);
    }

    [Fact]
    public void OverLockFlagShouldGoStaleWhenAnotherChatHoldsSlot()
    {
        // act
        var isFlagStale = CallScreensUI.IsOverLockFlagStale(ChatA, ChatA, isSameRing: true, ChatB);

        // assert
        isFlagStale.Should().BeTrue("only the slot holding the ring's own chat keeps its flag");
    }

    [Theory]
    [InlineData(CallPhase.Dialing, false)]
    [InlineData(CallPhase.Active, false)]
    [InlineData(CallPhase.Active, true)]
    public void CallScreenShouldBeTheSameWithAndWithoutVideo(CallPhase phase, bool hasVideo)
    {
        // arrange - a narrow call, not collapsed; its video, when there is one, sits in the inline panel mode
        var call = Call(CallRole.Caller, phase);
        var view = Decide(call, isNarrow: true);

        // act
        var screen = CallScreensUI.DecideScreen(view, hasVideo ? ChatA : null, VisualActivityPanelMode.Inline);

        // assert
        screen.Should().NotBeNull();
        screen!.ChatId.Should().Be(ChatA);
        screen.Call.Should().Be(call);
        screen.Mode.Should().Be(
            VisualActivityPanelMode.Expanded, "video starting or stopping must not move the call off its screen");
        screen.HasVideo.Should().Be(hasVideo);
    }

    [Fact]
    public void CallScreenShouldNotShowVideoOfAnotherChat()
    {
        // arrange
        var view = Decide(Call(CallRole.Caller, CallPhase.Active), isNarrow: true);

        // act
        var screen = CallScreensUI.DecideScreen(view, ChatB, VisualActivityPanelMode.Expanded);

        // assert
        screen!.ChatId.Should().Be(ChatA);
        screen.HasVideo.Should().BeFalse("the call's screen covers the chat whose video is being watched");
    }

    [Fact]
    public void OverLockScreenShouldCarryItsFlag()
    {
        // arrange
        var view = Decide(Call(CallRole.Callee, CallPhase.Active), isNarrow: true, Flags(overLock: ChatA));

        // act
        var screen = CallScreensUI.DecideScreen(view, ChatA, VisualActivityPanelMode.Inline);

        // assert
        screen!.IsOverLock.Should().BeTrue();
        screen.HasVideo.Should().BeTrue("the video shows over the lock screen, the chat doesn't have to");
    }

    [Theory]
    [InlineData(VisualActivityPanelMode.Inline)]
    [InlineData(VisualActivityPanelMode.Expanded)]
    [InlineData(VisualActivityPanelMode.Collapsed)]
    [InlineData(VisualActivityPanelMode.Hidden)]
    public void VideoWithoutCallShouldShowInItsPanelMode(VisualActivityPanelMode mode)
    {
        // act
        var screen = CallScreensUI.DecideScreen(CallView.None, ChatA, mode);

        // assert
        screen.Should().Be(new CallScreenState(ChatA, null, mode, true, false));
    }

    [Theory]
    [InlineData(VisualActivityPanelMode.Inline)]
    [InlineData(VisualActivityPanelMode.Expanded)]
    public void WideCallShouldHaveScreenOnlyForItsVideo(VisualActivityPanelMode mode)
    {
        // arrange - a wide active call has no view of its own
        var call = Call(CallRole.Callee, CallPhase.Active);
        var view = Decide(call, isNarrow: false);

        // act
        var audioOnlyScreen = CallScreensUI.DecideScreen(view, null, VisualActivityPanelMode.Inline);
        var videoScreen = CallScreensUI.DecideScreen(view, ChatA, mode);

        // assert
        audioOnlyScreen.Should().BeNull("a wide call without video stays in its chat");
        videoScreen.Should().Be(new CallScreenState(ChatA, call, mode, true, false));
    }

    [Fact]
    public void CollapsedCallShouldLeaveOnlyItsVideo()
    {
        // arrange
        var call = Call(CallRole.Caller, CallPhase.Active);
        var view = Decide(call, isNarrow: true, Flags(collapsed: ChatA));

        // act
        var audioOnlyScreen = CallScreensUI.DecideScreen(view, null, VisualActivityPanelMode.Inline);
        var videoScreen = CallScreensUI.DecideScreen(view, ChatA, VisualActivityPanelMode.Inline);

        // assert
        audioOnlyScreen.Should().BeNull("the island stands for a collapsed call that has no video");
        videoScreen!.Mode.Should().Be(VisualActivityPanelMode.Inline);
        videoScreen.Call.Should().Be(call, "the inline video is still the call's");
    }

    [Fact]
    public void RingShouldNotClaimVideoOfItsChat()
    {
        // arrange - a ring that isn't over the lock screen shows as a modal
        var view = Decide(Call(CallRole.Callee, CallPhase.Ringing), isNarrow: true);

        // act
        var screen = CallScreensUI.DecideScreen(view, ChatA, VisualActivityPanelMode.Expanded);

        // assert
        screen!.Call.Should().BeNull("the video being watched isn't the call's until it is answered");
    }

    private static CallView Decide(ActiveCall? call, bool isNarrow, CallScreenFlags flags = default)
        => CallScreensUI.DecideView(call, isNarrow, flags);

    private static CallScreenFlags Flags(ChatId? collapsed = null, ChatId? overLock = null)
        => new(collapsed, overLock);

    private static ActiveCall Call(CallRole origin, CallPhase phase)
        => new(ChatA, origin, phase, origin == CallRole.Callee ? CallerA : null, false);
}
