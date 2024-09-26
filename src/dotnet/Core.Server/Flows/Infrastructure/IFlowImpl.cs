namespace ActualChat.Flows.Infrastructure;

// Notes:
// - IFlowImpl:
// Each IFlowImpl is a separate Flow Type. 
// It means it is not a service itself, it is a type of a particular flow.
// Also, an abstract class implementing this interface assumes it has FlowId.
//
// (!) According to the FlowWorklet implementation this class is the Prototype pattern.
// (!) It seems that an implementation has to contain all "steps" neccessary to complete a flow.
// (!) Flow is a memento aswell. It must have it's state stored and be restored later.
//
// - FlowId:
// Apparently this interface implementation can not be on it's own. It heavily relies on
// the Flow abstract class and the FlowId associated with that. 
// The FlowId is transmits the following information: 
// a name of the type implementing a flow, method name to be called, serialized arguments
// to be used later. An assumption is that the step name in the Flow is the method name.
// This method should have no arguments and get all required parameters by parsing 
// the arguments string.
// 
// - FlowHost:
// It is a ShardWorker. Has a hardcoded sharding scheme.
// (?) Does it mean we're looking to remove other sharding schemes all together?
// 
// - FlowEventForwarder:
// (?) Seems like this class should handle forwarding of Commands to a Flow implementation.
// 
// - FlowWorklet:
// It is a shard of a Flow type.
// This flow worklet takes a IFlowImpl (!)Clone from a registry and actually runs it.
// (!) Danger. It uses unbound channel internally. Can fail in case of FlowEventForwarder
// sends commands to handle too fast.
//
// - FlowEventBin:
// It's only intend is to add a single property (IsHandled) to the state of an event.
// 
// - FlowData:
// (?) I assume it must store and pass some kind of information to a flow. 
// However it's not used anywhere, except DbFlows where it's called and effectively lost.
// 
public interface IFlowImpl
{
    FlowHost Host { get; }
    FlowWorklet Worklet { get; }
    FlowEventBin Event { get; }
}
