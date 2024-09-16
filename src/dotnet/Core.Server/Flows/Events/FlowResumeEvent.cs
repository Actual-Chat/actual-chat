using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record FlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId
) : IFlowSystemEvent;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record FlowHardResumeEvent(FlowId FlowId) : FlowResumeEvent(FlowId);
