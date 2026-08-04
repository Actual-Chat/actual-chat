using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a media attachment on a text chat entry.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ChatEntryAttachment(
    [property: DataMember, Key(0)] Symbol Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public ChatEntryId EntryId { get; init; } = null!;
    [DataMember, Key(3)] public int Index { get; init; }
    [DataMember, Key(4)] public MediaId MediaId { get; init; } = null!;
    [DataMember, Key(6)] public MediaId? ThumbnailMediaId { get; init; }

    // Populated only on reads
    [DataMember, Key(5)] public Media.Media Media { get; init; } = null!;
    [DataMember, Key(7)] public Media.Media? ThumbnailMedia { get; init; }

    public ChatEntryAttachment() : this(Symbol.Empty) { }

    [SerializationConstructor]
    public ChatEntryAttachment(
        Symbol Id,
        long Version,
        ChatEntryId entryId,
        int index,
        MediaId mediaId,
        Media.Media media,
        MediaId thumbnailMediaId,
        Media.Media thumbnailMedia)
        : this(Id, Version)
    {
        EntryId = entryId;
        Index = index;
        MediaId = mediaId;
        Media = media;
        ThumbnailMediaId = thumbnailMediaId;
        ThumbnailMedia = thumbnailMedia;
    }
}
