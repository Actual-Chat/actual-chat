namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    internal static SearchOutcome DecideSearch(ActiveCall? heldCall, ChatId foundChatId, bool isBusyAcked)
    {
        if (heldCall is null)
            return SearchOutcome.Claim;
        if (heldCall.ChatId == foundChatId)
            return SearchOutcome.None;

        return isBusyAcked ? SearchOutcome.None : SearchOutcome.Busy;
    }

    internal static HoldingAction DecideHolding(ActiveCall call, CallFacts facts, HoldingMemory memory)
    {
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

internal enum SearchOutcome { Claim, None, Busy }

internal enum HoldingAction { Keep, Join, Release }

// What the holding loop remembers about the call it holds across wake-ups.
internal readonly record struct HoldingMemory(bool HasSeenDialing, bool WasInConversation, bool IsDialingWaitOver)
{
    public HoldingMemory Observe(CallFacts facts)
        => this with {
            HasSeenDialing = HasSeenDialing || facts.Session == CallSessionState.Dialing,
            WasInConversation = WasInConversation || facts.IsInConversation,
        };
}
