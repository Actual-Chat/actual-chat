namespace ActualChat.Video;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record VideoFormat : MediaFormat
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public override MediaType Type => MediaType.Video;

    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)] public string Codec { get; init; } = "avc1"; // H.264 by default
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)] public int Width { get; init; }
    [DataMember(Order = 12), MemoryPackOrder(12), Key(12)] public int Height { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13), Key(13)] public string CodecSettings { get; init; } = "";
    // Source capture dimensions (getDisplayMedia output for screencast, camera sensor
    // for webcam). May be larger than encoder Width/Height when downscaling is active.
    // Server uses these to decide the quality-preset ceiling (e.g. unlock Ultra/4K).
    // Legacy peers that don't populate these send 0 — server falls back to Width/Height.
    [DataMember(Order = 14), MemoryPackOrder(14), Key(14)] public int SourceWidth { get; init; }
    [DataMember(Order = 15), MemoryPackOrder(15), Key(15)] public int SourceHeight { get; init; }

    public override byte[] Serialize(int index = 0)
        => CodecSettings.IsNullOrEmpty() ? [] : Convert.FromBase64String(CodecSettings);
}
