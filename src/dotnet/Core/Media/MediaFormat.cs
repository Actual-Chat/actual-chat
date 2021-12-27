namespace ActualChat.Media;

[DataContract]
public abstract record MediaFormat
{
    public abstract MediaType Type { get; }

    [DataMember(Order = 0)]
    public int ChannelCount { get; init; } = 1;

    public abstract byte[] Serialize(int index = 0);
}
