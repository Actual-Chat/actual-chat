using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);

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

    private static UserCall MyCall(ChatId chatId, CallRole role, CallPhase phase)
        => new() {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 2),
            Role = role,
            Phase = phase,
            PeerId = role == CallRole.Callee ? CallerA : null,
        };

    private static CallUI.CallIntentView Intent(ActiveCall? call, ChatId chatId, bool isFresh)
        => new(call, chatId, isFresh);

    private static ActiveCall Call(CallRole role, CallPhase phase)
        => new(ChatA, role, phase, role == CallRole.Callee ? CallerA : null, false);
}
