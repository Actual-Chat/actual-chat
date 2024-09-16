namespace ActualChat.Flows;

public interface IFlowControlEvent : IFlowEvent
{
    Symbol GetNextStep(Flow flow);
}
