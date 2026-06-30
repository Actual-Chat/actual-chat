using ActualChat.Media;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed visual media (photo / video / GIF) attachment shown in the right-panel Media tab.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VisualMediaItem : IChatContentItem
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, MemoryPackOrder(3), Key(3)] public int LocalIndex { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Moment At { get; init; }

    [DataMember, MemoryPackOrder(5), Key(5)] public required MediaId MediaId { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(7), Key(7)] public MediaId? ThumbnailMediaId { get; init; }
    [DataMember, MemoryPackOrder(8), Key(8)] public string ThumbnailBlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(9), Key(9)] public string ContentType { get; init; } = "";
    [DataMember, MemoryPackOrder(10), Key(10)] public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(11), Key(11)] public long Size { get; init; }
    [DataMember, MemoryPackOrder(12), Key(12)] public long DurationMs { get; init; }
}
