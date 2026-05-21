using ActualChat.Media;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// A single indexed piece of chat content (a photo, video, file, or link) shown in the
/// right-panel content tabs. Media fields are set for Photo/Video/File, link fields for Link.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatContentItem : IHasId<Symbol>, IHasVersion<long>
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatContentKind Kind { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, MemoryPackOrder(4), Key(4)] public int LocalIndex { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public Moment At { get; init; }

    // Media items (Photo / Video / File)
    [DataMember, MemoryPackOrder(6), Key(6)] public MediaId? MediaId { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(8), Key(8)] public MediaId? ThumbnailMediaId { get; init; }
    [DataMember, MemoryPackOrder(9), Key(9)] public string ThumbnailBlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(10), Key(10)] public string ContentType { get; init; } = "";
    [DataMember, MemoryPackOrder(11), Key(11)] public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(12), Key(12)] public long Size { get; init; }

    // Link items
    [DataMember, MemoryPackOrder(13), Key(13)] public Symbol LinkPreviewId { get; init; }
    [DataMember, MemoryPackOrder(14), Key(14)] public LinkPreview? LinkPreview { get; init; } // Populated only on reads

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => EntryId.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long EntryLocalId => EntryId.LocalId;
}
