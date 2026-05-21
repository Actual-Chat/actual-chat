namespace ActualChat.Notifications;

/// <summary>
/// Base for notifications about a specific chat entry — they are seen individually: the
/// similarity key is the <see cref="ChatEntryId"/>, from which <see cref="ChatId"/> and
/// <see cref="EntryLid"/> derive. Compare with <see cref="ChatEntryRelatedNotification"/>,
/// which references an entry but is keyed by (and collapses per) chat.
/// </summary>
[DataContract]
public abstract partial record ChatEntryNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId EntryId => ChatEntryId.Parse(SimilarityKey);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatId ChatId => EntryId.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long EntryLid => EntryId.LocalId;
}
