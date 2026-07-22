namespace ActualChat.Live;

public static class LiveFlows
{
    // The LiveConversationSummaryFlow's registered FlowDefs name (its type's simple name). Lets the
    // streaming backend wake the flow by FlowId on close without referencing the Chat.Service type.
    // Kept in sync with the flow by SummaryFlowNameMatchesConstant.
    public const string SummaryFlowName = "LiveConversationSummaryFlow";
}
