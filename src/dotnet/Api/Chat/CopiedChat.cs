using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record CopiedChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    )
    : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public ChatId SourceChatId { get; init; }
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }

    [DataMember, MemoryPackOrder(4)] public Moment LastCopyingAt { get; init; }
    [DataMember, MemoryPackOrder(5)] public long LastEntryId { get; init; }
    [DataMember, MemoryPackOrder(6)] public string LastCorrelationId { get; init; } = "";
    [DataMember, MemoryPackOrder(7)] public bool IsCopiedSuccessfully { get; init; }

    [DataMember, MemoryPackOrder(8)] public bool IsPublished { get; init; }
    [DataMember, MemoryPackOrder(9)] public Moment PublishedAt { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record CopiedChatDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public Option<ChatId> SourceChatId { get; init; }
    [DataMember, MemoryPackOrder(1)] public long? LastEntryId { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? LastCorrelationId { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool? IsCopiedSuccessfully { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool? IsPublished { get; init; }
}
