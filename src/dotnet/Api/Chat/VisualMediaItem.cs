using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

// Indexed visual media (photo / video / GIF) attachment shown in the right-panel Media tab.
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record VisualMediaItem : IChatContentItem
{
    [DataMember, Key(0)] public required Symbol Id { get; init; }
    [DataMember, Key(1)] public long Version { get; init; }
    [DataMember, Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, Key(3)] public int LocalIndex { get; init; }
    [DataMember, Key(4)] public Moment At { get; init; }

    [DataMember, Key(5)] public required MediaId MediaId { get; init; }
    [DataMember, Key(6)] public string BlobId { get; init; } = "";
    [DataMember, Key(7)] public MediaId? ThumbnailMediaId { get; init; }
    [DataMember, Key(8)] public string ThumbnailBlobId { get; init; } = "";
    [DataMember, Key(9)] public string ContentType { get; init; } = "";
    [DataMember, Key(10)] public string FileName { get; init; } = "";
    [DataMember, Key(11)] public long Size { get; init; }
    [DataMember, Key(12)] public long DurationMs { get; init; }
}
