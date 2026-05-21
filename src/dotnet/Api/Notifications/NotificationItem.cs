namespace ActualChat.Notifications;

/// <summary>
/// A single notification in a user's notification set. The union base carries only the
/// identity/dedup key (<see cref="NotificationId"/>) and display text — it does not assume
/// the notification is chat-related.
/// </summary>
[DataContract]
[Union(1, typeof(MessageNotificationItem))]
[Union(2, typeof(ReplyNotificationItem))]
[Union(3, typeof(InvitationNotificationItem))]
[Union(4, typeof(MentionNotificationItem))]
[Union(5, typeof(ReactionNotificationItem))]
[Union(6, typeof(AttentionNotificationItem))]
[Union(7, typeof(NewThreadNotificationItem))]
public abstract partial record NotificationItem(
    [property: DataMember(Order = 0), Key(0)] NotificationId Id,
    [property: DataMember(Order = 1), Key(1)] string Title,
    [property: DataMember(Order = 2), Key(2)] string Text,
    [property: DataMember(Order = 3), Key(3)] Moment CreatedAt
    ) : IHasId<NotificationId>
{
    // Computed properties
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;
}

[DataContract]
public abstract partial record ChatNotificationItem(
    NotificationId Id,
    string Title,
    string Text,
    Moment CreatedAt,
    [property: DataMember(Order = 4), Key(4)] long EntryLid
    ) : NotificationItem(Id, Title, Text, CreatedAt)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => field ??= ChatId.Parse(SimilarityKey);
}

[DataContract, MessagePackObject]
public sealed partial record MessageNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record ReplyNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record InvitationNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record MentionNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record ReactionNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record AttentionNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);

[DataContract, MessagePackObject]
public sealed partial record NewThreadNotificationItem(
    NotificationId Id, string Title, string Text, Moment CreatedAt, long EntryLid
    ) : ChatNotificationItem(Id, Title, Text, CreatedAt, EntryLid);
