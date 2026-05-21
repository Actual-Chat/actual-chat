namespace ActualChat.Notifications;

/// <summary>
/// Base for notifications about a specific chat entry. The similarity key is the
/// <see cref="ChatEntryId"/>, from which <see cref="ChatId"/> and <see cref="EntryLid"/> derive.
/// </summary>
[DataContract]
public abstract partial record ChatEntryNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatEntryId EntryId => ActualChat.ChatEntryId.Parse(SimilarityKey);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatId ChatId => EntryId.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override long EntryLid { get => EntryId.LocalId; init { } }
}
