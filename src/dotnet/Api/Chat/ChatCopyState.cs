using ActualLab.Versioning;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatCopyState(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    )
    : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2), Key(2)] public ChatId SourceChatId { get; init; } = null!;
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment CreatedAt { get; init; }

    [DataMember, MemoryPackOrder(4), Key(4)] public Moment LastCopyingAt { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public long LastProcessedEntryId { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public string LastCorrelationId { get; init; } = "";
    [DataMember, MemoryPackOrder(7), Key(7)] public bool IsCopiedSuccessfully { get; init; }

    [DataMember, MemoryPackOrder(8), Key(8)] public bool IsPublished { get; init; }
    [DataMember, MemoryPackOrder(9), Key(9)] public Moment PublishedAt { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatCopyStateDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public Option<ChatId> SourceChatId { get; init; }
    [DataMember, MemoryPackOrder(1)] public long? LastProcessedEntryId { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? LastCorrelationId { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool? IsCopiedSuccessfully { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool? IsPublished { get; init; }
}
