using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a media attachment on a text chat entry.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record TextEntryAttachment(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public TextEntryId EntryId { get; init; } = null!;
    [DataMember, MemoryPackOrder(3)] public int Index { get; init; }
    [DataMember, MemoryPackOrder(4)] public MediaId MediaId { get; init; } = null!;
    [DataMember, MemoryPackOrder(6)] public MediaId? ThumbnailMediaId { get; init; }

    // Populated only on reads
    [DataMember, MemoryPackOrder(5)] public Media.Media Media { get; init; } = null!;
    [DataMember, MemoryPackOrder(7)] public Media.Media? ThumbnailMedia { get; init; }

    public TextEntryAttachment() : this(Symbol.Empty) { }

    [MemoryPackConstructor, SerializationConstructor]
    public TextEntryAttachment(
        Symbol Id,
        long Version,
        TextEntryId entryId,
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
