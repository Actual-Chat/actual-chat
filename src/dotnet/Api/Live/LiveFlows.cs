namespace ActualChat.Live;

public static class LiveFlows
{
    // The LiveConversationSummaryFlow's registered FlowDefs name (its type's simple name). Lets the
    // streaming backend wake the flow by FlowId on close without referencing the Chat.Service type.
    // Kept in sync with the flow by SummaryFlowNameShouldMatchConstant.
    public const string SummaryFlowName = "LiveConversationSummaryFlow";

    // The CallTailFlow's registered FlowDefs name, scheduled by the streaming backend when a call's
    // conversation is materialized. Kept in sync with the flow by CallTailFlowNameShouldMatchConstant.
    public const string CallTailFlowName = "CallTailFlow";
}
