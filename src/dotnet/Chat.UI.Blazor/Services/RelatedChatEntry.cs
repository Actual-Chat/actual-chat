using MemoryPack;

namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record struct RelatedChatEntry(
    [property: DataMember, MemoryPackOrder(0)] RelatedEntryKind Kind,
    [property: DataMember, MemoryPackOrder(1)] ChatEntryId Id);

public enum RelatedEntryKind
{
    Reply,
    Edit,
}
