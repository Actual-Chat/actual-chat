namespace ActualChat.Notification;

[DataContract]
public record NotificationEntry(
    [property: DataMember] Symbol Id,
    [property: DataMember] NotificationKind Kind,
    [property: DataMember] string Title,
    [property: DataMember] string Content,
    [property: DataMember] string IconUrl,
    [property: DataMember] Moment NotificationTime
    ) : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public ChatNotification? ChatNotification { get; init; }
    [DataMember] public ChatEntryNotification? ChatEntryNotification { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => ChatNotification?.ChatId ?? ChatEntryNotification?.EntryId.ChatId ?? default(ChatId);
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
