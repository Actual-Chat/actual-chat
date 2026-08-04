namespace ActualChat.Streaming;

[DataContract, MessagePackObject]
public sealed partial record VideoStreamMemberInfo(
    [property: DataMember, Key(0)] ApiArray<string> SupportedDecoderCodecs,
    [property: DataMember, Key(1)] Moment RegisteredAt
);
