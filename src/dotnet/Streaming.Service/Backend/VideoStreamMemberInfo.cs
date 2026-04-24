namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VideoStreamMemberInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ApiArray<string> SupportedDecoderCodecs,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Moment RegisteredAt
);
