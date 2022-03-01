namespace ActualChat.Media;

[DataContract]
public abstract record MediaFormat
{
    public abstract MediaType Type { get; }

    public abstract byte[] Serialize(int index = 0);
}
