namespace ActualChat.Notifications;

/// <summary>
/// Notification about a conversation (live or regular). The similarity key is the
/// <see cref="ConversationId"/>, so all phases of one conversation collapse onto a single banner.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ConversationNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)] public long StartEntryLid { get; init; }
    [DataMember(Order = 10), Key(10)] public long EndEntryLid { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatId ChatId => ConversationId.Parse(SimilarityKey).ChatId;

    public static ConversationNotification New(UserId userId, ConversationId conversationId, long endEntryLid)
        => new(NotificationId.New(userId, NotificationKind.Conversation, conversationId.Value)) {
            StartEntryLid = conversationId.StartEntryLid,
            EndEntryLid = endEntryLid,
        };
}
