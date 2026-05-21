namespace ActualChat.Notifications;

/// <summary>
/// Base for notifications related to a chat. The similarity key is the <see cref="ChatId"/>.
/// </summary>
[DataContract]
public abstract partial record ChatNotification(NotificationId Id, long Version = 0)
    : Notification(Id, Version)
{
    [DataMember(Order = 10), Key(10)]
    public AuthorId? AuthorId { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public virtual ChatId ChatId => ChatId.Parse(SimilarityKey);
}
