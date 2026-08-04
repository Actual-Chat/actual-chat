namespace ActualChat.Flows;

[DataContract, MessagePackObject]
public sealed partial record FlowSummary(
    [property: DataMember, Key(0)] string FlowId,
    [property: DataMember, Key(1)] string Name,
    [property: DataMember, Key(2)] FlowStatus Status,
    [property: DataMember, Key(3)] long Version,
    [property: DataMember, Key(4)] Moment UpdatedAt);
