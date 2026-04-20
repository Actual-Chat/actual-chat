namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record VideoStreamMemberInfo(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<string> SupportedDecoderCodecs,
    [property: DataMember, MemoryPackOrder(1)] Moment RegisteredAt
);
