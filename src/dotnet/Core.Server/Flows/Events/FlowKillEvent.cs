using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record FlowKillEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null
) : ISystemFlowEvent;
