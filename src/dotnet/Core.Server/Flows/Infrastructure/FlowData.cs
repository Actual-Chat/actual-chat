namespace ActualChat.Flows.Infrastructure;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
// ReSharper disable once InconsistentNaming
public partial record struct FlowData(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long Version,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] Symbol Step,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] byte[]? Data
);
