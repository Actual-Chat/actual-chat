namespace ActualChat.Notification;

/// <summary>
/// A single notification in a user's notification set. The union base carries the
/// identity/dedup key (<see cref="NotificationId"/>) and the read-detection anchor.
/// </summary>
[DataContract]
[Union(0, typeof(ChatNotificationItem))]
[Union(1, typeof(AttentionNotificationItem))]
public abstract partial record NotificationItem(
    [property: DataMember(Order = 0), Key(0)] NotificationId Id,
    [property: DataMember(Order = 1), Key(1)] ChatId ChatId,
    [property: DataMember(Order = 2), Key(2)] string Title,
    [property: DataMember(Order = 3), Key(3)] string Text,
    [property: DataMember(Order = 4), Key(4)] Moment CreatedAt
    ) : IHasId<NotificationId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;

    // Local id of the entry that dismisses this notification once the user reads past it.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public abstract long ReadEntryLid { get; }
}

[DataContract, MessagePackObject]
public sealed partial record ChatNotificationItem(
    NotificationId Id,
    ChatId ChatId,
    string Title,
    string Text,
    Moment CreatedAt,
    [property: DataMember(Order = 5), Key(5)] long EntryLid,
    [property: DataMember(Order = 6), Key(6)] AuthorId AuthorId
    ) : NotificationItem(Id, ChatId, Title, Text, CreatedAt)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override long ReadEntryLid => EntryLid;
}

[DataContract, MessagePackObject]
public sealed partial record AttentionNotificationItem(
    NotificationId Id,
    ChatId ChatId,
    string Title,
    string Text,
    Moment CreatedAt,
    [property: DataMember(Order = 5), Key(5)] AuthorId CallerId,
    [property: DataMember(Order = 6), Key(6)] long LastEntryLid
    ) : NotificationItem(Id, ChatId, Title, Text, CreatedAt)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override long ReadEntryLid => LastEntryLid;
}
