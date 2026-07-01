namespace ActualChat.Notifications;

/// <summary>
/// An incoming voice/video call ring. The similarity key is the call's <see cref="ConversationId"/>,
/// so the ring and its later dismissal collapse onto a single banner.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record CallNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)] public bool HasVideo { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatId ChatId => ConversationId.Parse(SimilarityKey).ChatId;

    public static CallNotification New(UserId userId, ConversationId conversationId, AuthorId caller, bool hasVideo)
        => new(NotificationId.New(userId, NotificationKind.IncomingCall, conversationId.Value)) {
            AuthorId = caller,
            HasVideo = hasVideo,
        };
}
