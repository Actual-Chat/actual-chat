namespace ActualChat.Media;

[DataContract]
public abstract record MediaFormat
{
    public abstract MediaType Type { get; }

    [DataMember(Order = 0)]
    public int ChannelCount { get; init; }
    [DataMember(Order = 1)]
    public int SampleRate { get; init; }
}
