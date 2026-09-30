namespace ActualChat.Notifications;

/// <summary>
/// An incoming voice/video call ring. The similarity key is the call's <see cref="CallId"/>,
/// so the ring and its later dismissal collapse onto a single banner - and a dismissal that
/// arrives late can't take down the ring of the next call to the same chat.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record CallNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)] public bool HasVideo { get; init; }

    // A ring has no entry to read or see, so it keeps the Explicit default: it's cleared by
    // NotificationsBackend_CancelCall, and expiry is what covers a cancel that never arrives
    // (caller crashed, ring lapsed via RingTtl).
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt
        => SentAt + Constants.Call.RingTimeout + Constants.Notification.RingExpirationMargin;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ChatId ChatId => CallId.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public CallId CallId => CallId.Parse(SimilarityKey);

    public static CallNotification New(UserId userId, CallId callId, AuthorId caller, bool hasVideo)
        => new(NewId(userId, callId)) {
            AuthorId = caller,
            HasVideo = hasVideo,
        };

    public static NotificationId NewId(UserId userId, CallId callId)
        => NotificationId.New(userId, NotificationKind.IncomingCall, callId.Value);
}
