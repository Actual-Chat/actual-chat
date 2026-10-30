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
    // The captured frame's coded size at stream start, before the sender crops it to the ladder
    // shape and before the per-frame rotation: not the shape a viewer sees.
    [Obsolete("2026.10: Use InitialDisplaySize")]
    [DataMember(Order = 14), Key(14)] public Size2D SourceSize { get; init; }
    // The highest layer's size, upright, at stream start. Stale once the sender's device turns
    // or its ladder changes - use it only until the first frame arrives, then follow the frames.
    [DataMember(Order = 15), Key(15)] public Size2D InitialDisplaySize { get; init; }

    public override byte[] Serialize(int index = 0)
        => CodecSettings.IsNullOrEmpty() ? [] : Convert.FromBase64String(CodecSettings);
}
