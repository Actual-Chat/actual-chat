namespace ActualChat.Flows;

public interface ILegacyFlowControlEvent : IFlowEvent
{
    Symbol GetNextStep(LegacyFlow flow);
}
