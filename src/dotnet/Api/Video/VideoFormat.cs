namespace ActualChat.Video;

[DataContract, MessagePackObject]
public sealed partial record VideoFormat : MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public override MediaType Type => MediaType.Video;

    [DataMember(Order = 10), Key(10)] public string Codec { get; init; } = "avc1"; // H.264 by default
    [DataMember(Order = 11), Key(11)] public string CodecSettings { get; init; } = "";
    [DataMember(Order = 12), Key(12)] public byte LayerId { get; init; }
    [DataMember(Order = 13), Key(13)] public Size2D Size { get; init; }
    // Source capture dimensions (getDisplayMedia output for screencast, camera sensor for camera)
    [DataMember(Order = 14), Key(14)] public Size2D SourceSize { get; init; }

    public override byte[] Serialize(int index = 0)
        => CodecSettings.IsNullOrEmpty() ? [] : Convert.FromBase64String(CodecSettings);
}
