using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Notification.Notification

namespace ActualChat.Notification;

[DataContract]
public record Notification(
    [property: DataMember] NotificationId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, IUnionRecord<NotificationOption?>
{
    [DataMember] public UserId UserId { get; init; }
    [DataMember] public NotificationKind Kind { get; init; }
    [DataMember] public string Title { get; init; } = "";
    [DataMember] public string Content { get; init; } = "";
    [DataMember] public string IconUrl { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public Moment? HandledAt { get; init; }

    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public NotificationOption? Option { get; init; }

    [DataMember]
    public ChatNotificationOption? ChatNotification {
        get => Option as ChatNotificationOption;
        init => Option = value;
    }
    [DataMember]
    public ChatEntryNotificationOption? ChatEntryNotification {
        get => Option as ChatEntryNotificationOption;
        init => Option = value;
    }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsActive => HandledAt == null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => ChatEntryNotification?.EntryId.ChatId ?? ChatNotification?.ChatId ?? default(ChatId);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryId EntryId => ChatEntryNotification?.EntryId ?? default;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public AuthorId AuthorId => ChatEntryNotification?.AuthorId ?? default;

    public Notification() : this(NotificationId.None) { }
}

public abstract record NotificationOption : IRequirementTarget;

[DataContract]
public record ChatNotificationOption(
    [property: DataMember] ChatId ChatId
    ) : NotificationOption;

[DataContract]
public record ChatEntryNotificationOption(
    [property: DataMember] ChatEntryId EntryId,
    [property: DataMember] AuthorId AuthorId
    ) : NotificationOption;
