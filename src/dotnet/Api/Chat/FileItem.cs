using ActualChat.Media;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed non-visual file attachment shown in the right-panel Files tab.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FileItem : IChatContentItem
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, MemoryPackOrder(3), Key(3)] public int LocalIndex { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Moment At { get; init; }

    [DataMember, MemoryPackOrder(5), Key(5)] public required MediaId MediaId { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(7), Key(7)] public string ContentType { get; init; } = "";
    [DataMember, MemoryPackOrder(8), Key(8)] public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(9), Key(9)] public long Size { get; init; }
}
