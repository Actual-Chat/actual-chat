using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIDecisionsTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId CallerA = AuthorId.New(ChatA, 1);
    private static readonly IncomingCall RingA = new(ChatA, CallerA, false);

    [Fact]
    public void SearchShouldClaimFreeSlot()
    {
        // act
        var outcome = CallUI.DecideSearch(null, null, ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.Claim);
    }

    [Fact]
    public void SearchShouldIgnoreRingOfClaimedChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, null, ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.None, "the slot already belongs to this very ring");
    }

    [Fact]
    public void SearchShouldIgnoreRingOfHeldChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Active), ChatA, false);

        // assert
        outcome.Should().Be(SearchOutcome.None, "an accepted call's invite still reads Ringing for a moment");
    }

    [Fact]
    public void SearchShouldWaitWhileClaimIsUnconfirmed()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, null, ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Wait);
    }

    [Fact]
    public void SearchShouldAnswerBusyForAnotherChat()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Active), ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Busy);
    }

    [Fact]
    public void SearchShouldAnswerBusyDuringOutgoingCall()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Outgoing, CallPhase.Dialing), ChatB, false);

        // assert
        outcome.Should().Be(SearchOutcome.Busy);
    }

    [Fact]
    public void SearchShouldNotRepeatBusy()
    {
        // act
        var outcome = CallUI.DecideSearch(ChatA, Call(CallOrigin.Incoming, CallPhase.Ringing), ChatB, true);

        // assert
        outcome.Should().Be(SearchOutcome.None);
    }

    [Fact]
    public void HoldingShouldConfirmClaimedRing()
    {
        // act
        var action = CallUI.DecideHolding(null, Facts(RingA, CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Confirm);
    }

    [Fact]
    public void HoldingShouldReleaseClaimWithoutRing()
    {
        // act
        var action = CallUI.DecideHolding(null, Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldKeepRingingCall()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Ringing), Facts(RingA, CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Keep);
    }

    [Fact]
    public void HoldingShouldReleaseEndedRing()
    {
        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Incoming, CallPhase.Ringing), Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldJoinAnsweredOutgoingCall()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(session: CallSessionState.Connected), default);

        // assert
        action.Should().Be(HoldingAction.Join);
    }

    [Fact]
    public void HoldingShouldKeepDialingBeforeSessionAppears()
    {
        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), default);

        // assert
        action.Should().Be(HoldingAction.Keep, "the StartCall RPC may still be in flight");
    }

    [Fact]
    public void HoldingShouldReleaseDialingThatEnded()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: true, WasInConversation: false, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldReleaseDialingThatNeverAppeared()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: false, IsDialingWaitOver: true);

        // act
        var action = CallUI.DecideHolding(Call(CallOrigin.Outgoing, CallPhase.Dialing), Facts(), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldKeepAcceptedCallUntilAudioStarts()
    {
        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Active), Facts(session: CallSessionState.Dialing), default);

        // assert
        action.Should().Be(HoldingAction.Keep, "Accept commits Active before the audio starts");
    }

    [Fact]
    public void HoldingShouldReleaseWhenConversationLeft()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: true, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Incoming, CallPhase.Active), Facts(session: CallSessionState.Connected), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void HoldingShouldReleaseWhenSessionStopsBeingCall()
    {
        // arrange
        var memory = new HoldingMemory(HasSeenDialing: false, WasInConversation: true, IsDialingWaitOver: false);

        // act
        var action = CallUI.DecideHolding(
            Call(CallOrigin.Outgoing, CallPhase.Active), Facts(isInConversation: true), memory);

        // assert
        action.Should().Be(HoldingAction.Release);
    }

    [Fact]
    public void MemoryShouldRememberDialingAndConversation()
    {
        // act
        var memory = default(HoldingMemory)
            .Observe(Facts(session: CallSessionState.Dialing))
            .Observe(Facts(session: CallSessionState.Connected, isInConversation: true))
            .Observe(Facts());

        // assert
        memory.HasSeenDialing.Should().BeTrue();
        memory.WasInConversation.Should().BeTrue();
    }

    private static CallFacts Facts(
        IncomingCall? ring = null,
        CallSessionState session = CallSessionState.None,
        bool isInConversation = false)
        => new(ring, session, isInConversation);

    private static ActiveCall Call(CallOrigin origin, CallPhase phase)
        => new(ChatA, origin, phase, origin == CallOrigin.Incoming ? CallerA : null, false);
}
