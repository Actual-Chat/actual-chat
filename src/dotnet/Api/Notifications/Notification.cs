using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// Represents a user notification with content and metadata.
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Notifications.Notification

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record Notification(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] NotificationId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, IUnionRecord<NotificationOption?>, ISanitized
{
    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(7)]
    private ApiNullable8<Moment> MemoryPackHandledAt {
        get => HandledAt;
        init => HandledAt = value;
    }

    #endregion

    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public string Title { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public string Content { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public string IconUrl { get; init; } = "";
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public Moment CreatedAt { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public Moment SentAt { get; init; }
    [DataMember(Order = 7), MemoryPackIgnore, Key(7)]
    public Moment? HandledAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;

    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public NotificationOption? Option { get; init; }

    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public ChatNotificationOption? ChatNotification {
        get => Option as ChatNotificationOption;
        init => Option ??= value;
    }
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)]
    public ChatEntryNotificationOption? ChatEntryNotification {
        get => Option as ChatEntryNotificationOption;
        init => Option ??= value;
    }
    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public GetAttentionNotificationOption? GetAttentionNotification {
        get => Option as GetAttentionNotificationOption;
        init => Option ??= value;
    }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsActive => HandledAt == null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId? ChatId =>
        ChatEntryNotification?.EntryId.ChatId
        ?? GetAttentionNotification?.ChatId
        ?? ChatNotification?.ChatId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatEntryId? EntryId =>
        ChatEntryNotification?.EntryId
        ?? default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public AuthorId? AuthorId =>
        ChatEntryNotification?.AuthorId
        ?? GetAttentionNotification?.CallerId;

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

/// <summary>
/// Base class for notification type-specific data.
/// </summary>
public abstract record NotificationOption : IRequirementTarget;

/// <summary>
/// Notification data for a chat event.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChatNotificationOption(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId
    ) : NotificationOption;

/// <summary>
/// Notification data for a new chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChatEntryNotificationOption(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatEntryId EntryId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId
    ) : NotificationOption;

/// <summary>
/// Notification data for an attention request (e.g., incoming call).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record GetAttentionNotificationOption(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId CallerId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long LastEntryLocalId
) : NotificationOption;
