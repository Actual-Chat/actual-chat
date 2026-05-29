using ActualChat.Media;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed link extracted from a chat entry's markup, shown in the right-panel Links tab.
// Url is stored so the UI can always render at least a plain <a> even if the rich
// LinkPreview never resolves. LinkPreview is populated only on reads (via
// LinkPreviewsBackend.Get); not stored.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LinkItem : IChatContentItem
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, MemoryPackOrder(3), Key(3)] public int LocalIndex { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Moment At { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public string Url { get; init; } = "";

    [DataMember, MemoryPackOrder(6), Key(6)] public Symbol LinkPreviewId { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public LinkPreview? LinkPreview { get; init; }
}
