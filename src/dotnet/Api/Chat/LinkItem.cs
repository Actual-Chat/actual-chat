using ActualChat.Media;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed link extracted from a chat entry's markup, shown in the right-panel Links tab.
// Url is stored so the UI can always render at least a plain <a> even if the rich
// LinkPreview never resolves. LinkPreview is populated only on reads (via
// LinkPreviewsBackend.Get); not stored.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record LinkItem : IChatContentItem
{
    [DataMember, Key(0)] public required Symbol Id { get; init; }
    [DataMember, Key(1)] public long Version { get; init; }
    [DataMember, Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, Key(3)] public int LocalIndex { get; init; }
    [DataMember, Key(4)] public Moment At { get; init; }
    [DataMember, Key(5)] public string Url { get; init; } = "";

    [DataMember, Key(6)] public Symbol LinkPreviewId { get; init; }
    [DataMember, Key(7)] public LinkPreview? LinkPreview { get; init; }
}
