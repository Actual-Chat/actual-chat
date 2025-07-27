using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public enum RelatedEntryKind
{
    Reply,
    Edit,
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RelatedEntryRef(
    [property: DataMember, MemoryPackOrder(0)] RelatedEntryKind Kind,
    [property: DataMember, MemoryPackOrder(1)] EntryRef EntryRef)
{
    [IgnoreDataMember, MemoryPackIgnore]
    public TextEntryId EntryId => EntryRef.EntryId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record EntryRef([property: DataMember, MemoryPackOrder(0)] TextEntryId EntryId)
{
    [DataMember, MemoryPackOrder(1)]
    public ChatEntry? ChatEntry { get; init; }

    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => EntryId.ChatId;
}
