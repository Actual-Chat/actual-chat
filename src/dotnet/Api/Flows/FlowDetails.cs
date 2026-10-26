namespace ActualChat.Flows;

[DataContract, MessagePackObject]
public sealed partial record FlowDetails(
    [property: DataMember, Key(0)] string Console,
    [property: DataMember, Key(1)] string? Error,
    [property: DataMember, Key(2)] TimeSpan? DelayQuanta = null,
    [property: DataMember, Key(3)] TimeSpan? MaxResumeGap = null);
