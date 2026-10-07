namespace ActualChat.Flows;

// Marks a flow that reads its inbox (Flow.Inbox): the runtime loads it before the flow's Init and
// Resume, and the flow's commit stores the inbox changes in the same transaction as its state.
// Only such flows accept posted messages.

public interface IInboxProcessingFlow;
