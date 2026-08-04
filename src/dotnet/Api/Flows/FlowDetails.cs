namespace ActualChat.Flows;

[DataContract, MessagePackObject]
public sealed partial record FlowDetails(
    [property: DataMember, Key(0)] string Console,
    [property: DataMember, Key(1)] string? Error);
