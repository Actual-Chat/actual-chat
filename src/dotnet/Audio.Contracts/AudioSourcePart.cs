namespace ActualChat.Audio;

[DataContract]
public record AudioSourcePart(
    [property: DataMember(Order = 0)] AudioFormat? Format,
    [property: DataMember(Order = 1)] AudioFrame? Frame,
    [property: DataMember(Order = 2)] TimeSpan? Duration
);
