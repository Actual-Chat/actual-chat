namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record VideoStreamMemberInfo(
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] ApiArray<string> SupportedDecoderCodecs,
    [property: DataMember, MemoryPackOrder(1), NbKey(1)] Moment RegisteredAt
);
