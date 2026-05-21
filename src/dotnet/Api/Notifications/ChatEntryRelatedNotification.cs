namespace ActualChat.Notifications;

/// <summary>
/// Base for chat notifications that reference an entry but collapse per chat: the similarity
/// key is the <see cref="ChatId"/> and <see cref="EntryLid"/> is stored. Compare with
/// <see cref="ChatEntryNotification"/>, whose similarity key is the entry itself.
/// </summary>
[DataContract]
public abstract partial record ChatEntryRelatedNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)]
    public long EntryLid { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId EntryId => ChatEntryId.New(ChatId, EntryLid);
}
