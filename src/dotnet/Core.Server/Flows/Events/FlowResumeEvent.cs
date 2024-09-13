namespace ActualChat.Flows;

public record FlowResumeEvent(FlowId FlowId, int Index) : IFlowEvent;
