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

    [Fact]
    public void NoCallAndNoIntentShouldLeaveTheSlotEmpty()
    {
        // act
        var call = CallUI.Reconcile(null, default);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void ServerCallShouldFillTheSlot()
    {
        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Ringing), default);

        // assert
        call.Should().NotBeNull();
        call!.ChatId.Should().Be(ChatA);
        call.Role.Should().Be(CallRole.Callee);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void FreshIntentShouldSurviveAnEmptyAnswer()
    {
        // arrange — the answer a disconnected client reads is "no call"
        var intent = Intent(Call(CallRole.Caller, CallPhase.Dialing), ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(null, intent);

        // assert — this is #4532's failure mode: an empty read must not drop a just-started call
        call.Should().NotBeNull();
        call!.ChatId.Should().Be(ChatA);
    }

    [Fact]
    public void StaleIntentShouldNotSurviveAnEmptyAnswer()
    {
        // arrange
        var intent = Intent(Call(CallRole.Caller, CallPhase.Dialing), ChatA, isFresh: false);

        // act
        var call = CallUI.Reconcile(null, intent);

        // assert
        call.Should().BeNull("a call the server doesn't know about is over once the grace lapses");
    }

    [Fact]
    public void JustAcceptedRingShouldKeepItsPhaseUntilTheServerCatchesUp()
    {
        // arrange — Accept commits Active locally before its RPC lands
        var intent = Intent(Call(CallRole.Callee, CallPhase.Active), ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Ringing), intent);

        // assert — the screens must not blink back to ringing
        call!.Phase.Should().Be(CallPhase.Active);
    }

    [Fact]
    public void JustLeftCallShouldStayGoneWhileTheServerStillNamesIt()
    {
        // arrange — hanging up clears the slot before the server sees the presence go
        var intent = Intent(null, ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Active), intent);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void JustLeftCallShouldStayGoneWhileTheServerStillNamesItById()
    {
        // arrange
        var intent = Intent(null, ChatA, isFresh: true, leftCallId: Call1);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Active, Call1), intent);

        // assert
        call.Should().BeNull();
    }

    [Fact]
    public void NextCallToTheChatJustLeftShouldNotWaitTheGraceOut()
    {
        // arrange - the peer calls back right after the hang-up
        var intent = Intent(null, ChatA, isFresh: true, leftCallId: Call1);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Ringing, Call2), intent);

        // assert - by chat alone this ring reads as the call just left, and stays hidden
        call.Should().NotBeNull();
        call!.CallId.Should().Be(Call2);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void CallPlacedRightAfterAHangUpShouldNotBeReplacedByTheCallJustLeft()
    {
        // arrange - the server still names the call this client hung up on
        var redial = Call(CallRole.Caller, CallPhase.Dialing);
        var intent = Intent(redial, ChatA, isFresh: true, leftCallId: Call1);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Caller, CallPhase.Active, Call1), intent);

        // assert
        call.Should().Be(redial);
    }

    [Fact]
    public void RejoinedCallShouldFollowTheServerOnceItIsNoLongerTheLeftOne()
    {
        // arrange - the redial joined the call just left, and naming it cleared "left"
        var redial = Call(CallRole.Caller, CallPhase.Dialing) with { CallId = Call1 };
        var intent = Intent(redial, ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Caller, CallPhase.Active, Call1), intent);

        // assert - held as "left", it would sit at Dialing with no audio for the whole grace
        call!.Phase.Should().Be(CallPhase.Active);
    }

    [Fact]
    public void ServerShouldNameTheCallAnIntentHolds()
    {
        // arrange - a call placed here holds the slot before the server has named it
        var intent = Intent(Call(CallRole.Caller, CallPhase.Dialing), ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Caller, CallPhase.Dialing, Call1), intent);

        // assert
        call!.CallId.Should().Be(Call1);
    }

    [Fact]
    public void JustAcceptedRingShouldNotHoldItsPhaseOverAnotherCall()
    {
        // arrange - the ring answered is over, and the chat rings in the next call
        var accepted = Call(CallRole.Callee, CallPhase.Active) with { CallId = Call1 };
        var intent = Intent(accepted, ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Ringing, Call2), intent);

        // assert
        call!.CallId.Should().Be(Call2);
        call.Phase.Should().Be(CallPhase.Ringing);
    }

    [Fact]
    public void ServerShouldWinOverAnIntentForAnotherChat()
    {
        // arrange — the local claim lost the arbitration; the server put me in another call
        var intent = Intent(Call(CallRole.Caller, CallPhase.Dialing), ChatA, isFresh: true);

        // act
        var call = CallUI.Reconcile(MyCall(ChatB, CallRole.Callee, CallPhase.Ringing), intent);

        // assert
        call!.ChatId.Should().Be(ChatB);
    }

    [Fact]
    public void StaleIntentShouldNotHoldAPhaseTheServerMovedOn()
    {
        // arrange
        var intent = Intent(Call(CallRole.Callee, CallPhase.Active), ChatA, isFresh: false);

        // act
        var call = CallUI.Reconcile(MyCall(ChatA, CallRole.Callee, CallPhase.Ringing), intent);

        // assert
        call!.Phase.Should().Be(CallPhase.Ringing);
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
            CallId = callId,
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 2),
            Role = role,
            Phase = phase,
            PeerId = role == CallRole.Callee ? CallerA : null,
        };

    private static CallUI.CallIntentView Intent(
        ActiveCall? call,
        ChatId chatId,
        bool isFresh,
        CallId? leftCallId = null)
        => new(call, chatId, isFresh, leftCallId);

    private static ActiveCall Call(CallRole role, CallPhase phase)
        => new(ChatA, role, phase, role == CallRole.Callee ? CallerA : null, false);
}
