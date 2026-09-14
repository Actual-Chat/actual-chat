namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    internal static SearchOutcome DecideSearch(
        ChatId? slotChatId,
        ActiveCall? activeCall,
        ChatId foundChatId,
        bool isBusyAcked)
    {
        if (slotChatId is null)
            return SearchOutcome.Claim;
        if (slotChatId == foundChatId)
            return SearchOutcome.None;
        if (activeCall is null)
            return SearchOutcome.Wait;

        return isBusyAcked ? SearchOutcome.None : SearchOutcome.Busy;
    }

    internal static HoldingAction DecideHolding(ActiveCall? call, CallFacts facts, HoldingMemory memory)
    {
        if (call is null)
            return facts.Ring is not null ? HoldingAction.Confirm : HoldingAction.Release;

        switch (call.Phase) {
        case CallPhase.Ringing:
            return facts.Ring is not null ? HoldingAction.Keep : HoldingAction.Release;
        case CallPhase.Dialing:
            if (facts.Session == CallSessionState.Connected)
                return HoldingAction.Join;
            if (facts.Session == CallSessionState.Dialing)
                return HoldingAction.Keep;

            // No session yet may just be the StartCall RPC in flight: only one seen and gone, or one
            // that never showed up in time, ends the call.
            return memory.HasSeenDialing || memory.IsDialingWaitOver ? HoldingAction.Release : HoldingAction.Keep;
        default:
            if (facts.Session == CallSessionState.None)
                return HoldingAction.Release;

            // Accept commits Active before the audio starts, so only leaving the conversation ends the call.
            return memory.WasInConversation && !facts.IsInConversation
                ? HoldingAction.Release
                : HoldingAction.Keep;
        }
    }
}

internal enum SearchOutcome { Claim, Wait, None, Busy }

internal enum HoldingAction { Keep, Confirm, Join, Release }

// What the holding loop remembers about the call it holds across wake-ups.
internal readonly record struct HoldingMemory(bool HasSeenDialing, bool WasInConversation, bool IsDialingWaitOver)
{
    public HoldingMemory Observe(CallFacts facts)
        => this with {
            HasSeenDialing = HasSeenDialing || facts.Session == CallSessionState.Dialing,
            WasInConversation = WasInConversation || facts.IsInConversation,
        };
}
