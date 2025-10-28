using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;

namespace ActualChat.Flows;

public static class FlowEventExt
{
    private static readonly UuidGenerator UuidGenerator = UlidUuidGenerator.Instance;

    public static OperationEvent ToOperationEvent(this IFlowEvent @event, Moment now)
    {
        var operationEvent = new OperationEvent("", @event) {
            LoggedAt = now,
        };
        if (@event is IHasDelayUntil hasDelayUntil) {
            if (hasDelayUntil is IHasDelayUntilQuanta duq && duq.DelayQuanta > TimeSpan.Zero) {
                var uuidPrefix = $"{@event.GetType().GetName()}({@event.FlowId.Value})";
                operationEvent.SetDelayUntil(duq.DelayUntil, duq.DelayQuanta, uuidPrefix);
            }
            else {
                operationEvent.Uuid = $"{@event.GetType().GetName()}-{UuidGenerator.Next()}";
                operationEvent.DelayUntil = hasDelayUntil.DelayUntil;
            }
        }
        else
            operationEvent.Uuid = $"{@event.GetType().GetName()}-{UuidGenerator.Next()}";
        return operationEvent;
    }
}
