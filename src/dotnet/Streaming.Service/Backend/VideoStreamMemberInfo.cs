namespace ActualChat.Streaming;

[DataContract, MessagePackObject]
public sealed partial record VideoStreamMemberInfo(
    [property: DataMember, Key(0)] ApiArray<string> SupportedDecoderCodecs,
    [property: DataMember, Key(1)] Moment RegisteredAt,
    // Only an admin may pin the call's codec — see ChatState.ForcedCodecMarker.
    [property: DataMember, Key(2)] bool IsAdmin = false
);
