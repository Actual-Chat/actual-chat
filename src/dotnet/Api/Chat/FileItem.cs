using ActualChat.Media;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed non-visual file attachment shown in the right-panel Files tab.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record FileItem : IChatContentItem
{
    [DataMember, Key(0)] public required Symbol Id { get; init; }
    [DataMember, Key(1)] public long Version { get; init; }
    [DataMember, Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, Key(3)] public int LocalIndex { get; init; }
    [DataMember, Key(4)] public Moment At { get; init; }

    [DataMember, Key(5)] public required MediaId MediaId { get; init; }
    [DataMember, Key(6)] public string BlobId { get; init; } = "";
    [DataMember, Key(7)] public string ContentType { get; init; } = "";
    [DataMember, Key(8)] public string FileName { get; init; } = "";
    [DataMember, Key(9)] public long Size { get; init; }
}
