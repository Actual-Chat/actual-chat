namespace ActualChat.Media;

/// <summary>
/// Base class for media format descriptors (audio, video).
/// </summary>
[DataContract]
public abstract record MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public abstract MediaType Type { get; }

    public abstract byte[] Serialize(int index = 0);
}
