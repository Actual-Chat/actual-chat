using ActualLab.Versioning;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public partial record ChatCopyState(
    [property: DataMember, Key(0)] ChatId Id,
    [property: DataMember, Key(1)] long Version = 0
    )
    : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public ChatId SourceChatId { get; init; } = null!;
    [DataMember, Key(3)] public Moment CreatedAt { get; init; }

    [DataMember, Key(4)] public Moment LastCopyingAt { get; init; }
    [DataMember, Key(5)] public long LastProcessedEntryId { get; init; }
    [DataMember, Key(6)] public string LastCorrelationId { get; init; } = "";
    [DataMember, Key(7)] public bool IsCopiedSuccessfully { get; init; }

    [DataMember, Key(8)] public bool IsPublished { get; init; }
    [DataMember, Key(9)] public Moment PublishedAt { get; init; }
}

[DataContract, MessagePackObject(true)]
public sealed partial record ChatCopyStateDiff : RecordDiff
{
    [DataMember] public Option<ChatId> SourceChatId { get; init; }
    [DataMember] public long? LastProcessedEntryId { get; init; }
    [DataMember] public string? LastCorrelationId { get; init; }
    [DataMember] public bool? IsCopiedSuccessfully { get; init; }
    [DataMember] public bool? IsPublished { get; init; }
}
