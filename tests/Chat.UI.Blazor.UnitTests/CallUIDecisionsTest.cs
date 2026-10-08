using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);
    private static readonly CallId Call1 = CallId.New(ChatA, "1");
    private static readonly CallId Call2 = CallId.New(ChatA, "2");
    private static readonly CallUI.CallGestures NoGestures = new([]);

    [Fact]
    public void NoCallAndAnEmptySlotShouldStayEmpty()
    {
        // act
        var call = CallUI.Reconcile(null, null, NoGestures);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void RingShouldFillTheSlot()
    {
        // act
        var call = CallUI.Reconcile(null, MyCall(ChatA, CallRole.Callee, CallPhase.Ringing), NoGestures);

        // assert
        call.Should().NotBeNull();
        call!.ChatId.Should().Be(ChatA);
        call.Role.Should().Be(CallRole.Callee);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void NoCallShouldNotDropACallStillBeingPlaced()
    {
        // arrange — a StartCall resent over a server restart took 14 s to land (#5115)
        var placing = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var call = CallUI.Reconcile(placing, null, NoGestures);

        // assert
        call.Should().Be(placing, "the server hasn't been told of this call yet, so it can't say it's over");
    }

    [Fact]
    public void NoCallShouldEndACallTheServerNamed()
    {
        // arrange — e.g. a ring answered on another device, or an end the client was offline too long to read
        var held = Call(CallRole.Callee, CallPhase.Ringing) with { CallId = Call1 };

        // act
        var call = CallUI.Reconcile(held, null, NoGestures);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void EndShouldEndTheCallItNames()
    {
        // arrange
        var held = Call(CallRole.Callee, CallPhase.Active) with { CallId = Call1 };

        // act
        var call = CallUI.Reconcile(held, MyCall(ChatA, CallRole.Callee, CallPhase.Ended, Call1), NoGestures);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void EndOfTheLastCallShouldNotDropTheNextOneBeingPlaced()
    {
        // arrange — a redial whose StartCall hasn't answered yet, while the server still reports the last end
        var redial = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var call = CallUI.Reconcile(redial, MyCall(ChatA, CallRole.Caller, CallPhase.Ended, Call1), NoGestures);

        // assert
        call.Should().Be(redial);
    }

    [Fact]
    public void AnswerOnItsWayShouldOutliveTheEndOfItsRing()
    {
        // arrange — a late answer: the ring's claim ended, and the answer's AcceptCall takes it back
        var accepted = Call(CallRole.Callee, CallPhase.Active) with { CallId = Call1 };
        var gestures = new CallUI.CallGestures([], AcceptingCallId: Call1);

        // act
        var onEnd = CallUI.Reconcile(accepted, MyCall(ChatA, CallRole.Callee, CallPhase.Ended, Call1), gestures);
        var onNothing = CallUI.Reconcile(accepted, null, gestures);

        // assert
        onEnd.Should().Be(accepted);
        onNothing.Should().Be(accepted);
    }

    [Fact]
    public void ServerShouldNameTheCallBeingPlaced()
    {
        // arrange
        var placing = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var call = CallUI.Reconcile(placing, MyCall(ChatA, CallRole.Caller, CallPhase.Dialing, Call1), NoGestures);

        // assert
        call!.CallId.Should().Be(Call1);
    }

    [Fact]
    public void JustAcceptedRingShouldKeepItsPhaseUntilTheServerCatchesUp()
    {
        // arrange — Accept commits Active locally before its RPC lands
        var accepted = Call(CallRole.Callee, CallPhase.Active) with { CallId = Call1 };

        // act
        var call = CallUI.Reconcile(accepted, MyCall(ChatA, CallRole.Callee, CallPhase.Ringing, Call1), NoGestures);

        // assert — the screens must not blink back to ringing
        call!.Phase.Should().Be(CallPhase.Active);
    }

    [Fact]
    public void JustLeftCallShouldStayGoneWhileTheServerStillNamesIt()
    {
        // arrange — hanging up clears the slot before the server sees the presence go
        var gestures = new CallUI.CallGestures([Call1]);

        // act
        var call = CallUI.Reconcile(null, MyCall(ChatA, CallRole.Callee, CallPhase.Active, Call1), gestures);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void CallCancelledBeforeItWasNamedShouldStayGone()
    {
        // arrange — its StartCall's answer, the id, is still on its way
        var gestures = new CallUI.CallGestures([], CancelledChatId: ChatA);

        // act
        var call = CallUI.Reconcile(null, MyCall(ChatA, CallRole.Caller, CallPhase.Dialing, Call1), gestures);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void NextCallToTheChatJustLeftShouldShowAtOnce()
    {
        // arrange - the peer calls back right after the hang-up
        var gestures = new CallUI.CallGestures([Call1]);

        // act
        var call = CallUI.Reconcile(null, MyCall(ChatA, CallRole.Callee, CallPhase.Ringing, Call2), gestures);

        // assert
        call!.CallId.Should().Be(Call2);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void CallPlacedRightAfterAHangUpShouldNotBeReplacedByTheCallJustLeft()
    {
        // arrange - the server still names the call this client hung up on
        var redial = Call(CallRole.Caller, CallPhase.Dialing);
        var gestures = new CallUI.CallGestures([Call1]);

        // act
        var call = CallUI.Reconcile(redial, MyCall(ChatA, CallRole.Caller, CallPhase.Active, Call1), gestures);

        // assert
        call.Should().Be(redial);
    }

    [Fact]
    public void JustAcceptedRingShouldNotHoldItsPhaseOverAnotherCall()
    {
        // arrange - the ring answered is over, and the chat rings in the next call
        var accepted = Call(CallRole.Callee, CallPhase.Active) with { CallId = Call1 };

        // act
        var call = CallUI.Reconcile(accepted, MyCall(ChatA, CallRole.Callee, CallPhase.Ringing, Call2), NoGestures);

        // assert
        call!.CallId.Should().Be(Call2);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void ServerShouldWinOverAGestureInAnotherChat()
    {
        // arrange — the local claim lost the arbitration; the server put me in another call
        var placing = Call(CallRole.Caller, CallPhase.Dialing);

        // act
        var call = CallUI.Reconcile(placing, MyCall(ChatB, CallRole.Callee, CallPhase.Ringing), NoGestures);

        // assert
        call!.ChatId.Should().Be(ChatB);
    }

    [Fact]
    public void AnsweredOutgoingCallShouldPutTheCallerOnTheLine()
    {
        // act
        var shouldStart = CallUI.ShouldStartCallAudio(
            Call(CallRole.Caller, CallPhase.Dialing), Call(CallRole.Caller, CallPhase.Active));

        // assert
        shouldStart.Should().BeTrue();
    }

    [Fact]
    public void AnAlreadyAnsweredCallShouldNotStartItsAudioAgain()
    {
        // arrange — the same call, with the peer the server has now named
        var held = new ActiveCall(ChatA, CallRole.Caller, CallPhase.Active, null, false);
        var next = new ActiveCall(ChatA, CallRole.Caller, CallPhase.Active, CallerA, false);

        // act
        var shouldStart = CallUI.ShouldStartCallAudio(held, next);

        // assert
        shouldStart.Should().BeFalse();
    }

    [Fact]
    public void ASecondCallToTheSameChatShouldStartItsAudio()
    {
        // arrange — the first one ended with this client freeing the slot, so nothing is held
        var next = Call(CallRole.Caller, CallPhase.Active);

        // act
        var shouldStart = CallUI.ShouldStartCallAudio(null, next);

        // assert — a latch left over from the first call is what left the second one silent
        shouldStart.Should().BeTrue();
    }

    [Fact]
    public void AnotherAnsweredCallToTheSameChatShouldStartItsAudio()
    {
        // arrange - the slot went from one answered call straight to the next, with no release between
        var held = Call(CallRole.Caller, CallPhase.Active) with { CallId = Call1 };
        var next = Call(CallRole.Caller, CallPhase.Active) with { CallId = Call2 };

        // act
        var shouldStart = CallUI.ShouldStartCallAudio(held, next);

        // assert
        shouldStart.Should().BeTrue();
    }

    [Fact]
    public void NamingTheHeldCallShouldNotStartItsAudioAgain()
    {
        // arrange
        var held = Call(CallRole.Caller, CallPhase.Active);
        var next = held with { CallId = Call1 };

        // act
        var shouldStart = CallUI.ShouldStartCallAudio(held, next);

        // assert
        shouldStart.Should().BeFalse();
    }

    [Fact]
    public void AnAnsweredRingShouldNotStartTheCallerSideAudio()
    {
        // act
        var shouldStart = CallUI.ShouldStartCallAudio(
            Call(CallRole.Callee, CallPhase.Ringing), Call(CallRole.Callee, CallPhase.Active));

        // assert — the callee joins through Accept, not through the slot
        shouldStart.Should().BeFalse();
    }

    private static UserCall MyCall(ChatId chatId, CallRole role, CallPhase phase, CallId? callId = null)
        => new() {
            CallId = callId ?? CallId.New(chatId, "0"),
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 2),
            Role = role,
            Phase = phase,
            PeerId = role == CallRole.Callee ? CallerA : null,
        };

    private static ActiveCall Call(CallRole role, CallPhase phase)
        => new(ChatA, role, phase, role == CallRole.Callee ? CallerA : null, false);
}
