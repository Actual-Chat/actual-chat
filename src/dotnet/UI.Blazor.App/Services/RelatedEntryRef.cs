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
    [property: DataMember, MemoryPackOrder(1)] TextEntryId EntryId);
