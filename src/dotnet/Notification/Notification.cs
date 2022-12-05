using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Notification.Notification

namespace ActualChat.Notification;

[DataContract]
public record Notification(
    [property: DataMember] NotificationId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public UserId UserId { get; init; }
    [DataMember] public NotificationKind Kind { get; init; }
    [DataMember] public string Title { get; init; } = "";
    [DataMember] public string Content { get; init; } = "";
    [DataMember] public string IconUrl { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public Moment? HandledAt { get; init; }
    [DataMember] public ChatNotification? ChatNotification { get; init; }
    [DataMember] public ChatEntryNotification? ChatEntryNotification { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsActive => HandledAt == null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => ChatEntryNotification?.EntryId.ChatId ?? ChatNotification?.ChatId ?? default(ChatId);
}

[DataContract]
public record ChatNotification(
    [property: DataMember] ChatId ChatId
    ) : IRequirementTarget;

[DataContract]
public record ChatEntryNotification(
    [property: DataMember] ChatEntryId EntryId,
    [property: DataMember] AuthorId AuthorId
    ) : IRequirementTarget;
