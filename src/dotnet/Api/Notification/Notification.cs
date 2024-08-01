using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Notification;

#pragma warning disable MA0049 // Allows ActualChat.Notification.Notification

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Notification(
    [property: DataMember, MemoryPackOrder(0)] NotificationId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, IUnionRecord<NotificationOption?>
{
    [DataMember, MemoryPackOrder(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string IconUrl { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment SentAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public Moment? HandledAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol SimilarityKey => Id.SimilarityKey;

    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public NotificationOption? Option { get; init; }

    [DataMember, MemoryPackOrder(8)]
    public ChatNotificationOption? ChatNotification {
        get => Option as ChatNotificationOption;
        init => Option ??= value;
    }
    [DataMember, MemoryPackOrder(9)]
    public ChatEntryNotificationOption? ChatEntryNotification {
        get => Option as ChatEntryNotificationOption;
        init => Option ??= value;
    }
    [DataMember, MemoryPackOrder(10)]
    public GetAttentionNotificationOption? GetAttentionNotification {
        get => Option as GetAttentionNotificationOption;
        init => Option ??= value;
    }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsActive => HandledAt == null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId =>
        ChatEntryNotification?.EntryId.ChatId
        ?? GetAttentionNotification?.ChatId
        ?? ChatNotification?.ChatId
        ?? default;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId EntryId =>
        ChatEntryNotification?.EntryId
        ?? default;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AuthorId AuthorId =>
        ChatEntryNotification?.AuthorId
        ?? GetAttentionNotification?.CallerId
        ?? default;

    public Notification WithSimilar(Notification similar)
    {
        if (Id != similar.Id)
            throw new ArgumentOutOfRangeException(nameof(similar));

        return this with {
            Version = similar.Version,
            CreatedAt = similar.CreatedAt,
            HandledAt = null,
        };
    }
}

public abstract record NotificationOption : IRequirementTarget;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatNotificationOption(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
    ) : NotificationOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatEntryNotificationOption(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId EntryId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId AuthorId
    ) : NotificationOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record GetAttentionNotificationOption(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId CallerId,
    [property: DataMember, MemoryPackOrder(2)] long LastEntryLocalId
) : NotificationOption;
