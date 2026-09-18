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
    public void UnconfirmedDialingShouldShowNothingButKeepCall()
    {
        // arrange
        var call = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var view = Decide(call, isNarrow: true, isDialingConfirmed: false);

        // assert
        view.Kind.Should().Be(CallViewKind.None, "there's no invitee to show before the server's dialing");
        view.Call.Should().Be(call, "the view loop tells a released slot by the call going away");
    }

    [Fact]
    public void UnconfirmedDialingShouldIgnoreCollapse()
    {
        // arrange
        var call = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var view = Decide(call, isNarrow: true, Flags(collapsed: ChatA), isDialingConfirmed: false);

        // assert
        view.Kind.Should().Be(CallViewKind.None, "the island names the invitee too, so it waits as well");
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

    [Fact]
    public void CollapseShouldNotHideActiveCall()
    {
        // act
        var view = Decide(Call(CallRole.Caller, CallPhase.Active), isNarrow: true, Flags(collapsed: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen, "an answered collapsed call gets its full-screen view");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InChatShouldHideActiveCall(bool isNarrow)
    {
        // act
        var view = Decide(Call(CallRole.Callee, CallPhase.Active), isNarrow, Flags(inChat: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.None);
    }

    [Fact]
    public void InChatShouldNotHideDialing()
    {
        // act
        var view = Decide(Call(CallRole.Caller, CallPhase.Dialing), isNarrow: true, Flags(inChat: ChatA));

        // assert
        view.Kind.Should().Be(CallViewKind.FullScreen);
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
    public void OverLockShouldOverrideCollapseAndInChat(CallPhase phase)
    {
        // arrange
        var flags = Flags(collapsed: ChatA, inChat: ChatA, overLock: ChatA);

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
        var flags = Flags(collapsed: ChatB, inChat: ChatB, overLock: ChatB);

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

    private static CallView Decide(
        ActiveCall? call,
        bool isNarrow,
        CallScreenFlags flags = default,
        bool isDialingConfirmed = true)
        => CallScreensUI.DecideView(call, isDialingConfirmed, isNarrow, flags);

    private static CallScreenFlags Flags(ChatId? collapsed = null, ChatId? inChat = null, ChatId? overLock = null)
        => new(collapsed, inChat, overLock);

    private static ActiveCall Call(CallRole origin, CallPhase phase)
        => new(ChatA, origin, phase, origin == CallRole.Callee ? CallerA : null, false);
}
