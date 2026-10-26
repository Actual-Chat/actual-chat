namespace ActualChat.Flows;

[DataContract, MessagePackObject]
public sealed partial record FlowsQuery(
    [property: DataMember, Key(0)] string? Name = null,
    [property: DataMember, Key(2)] int Limit = 100,
    [property: DataMember, Key(4)] FlowListScope Scope = FlowListScope.Active);
