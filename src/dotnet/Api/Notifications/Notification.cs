using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// A single notification in a user's notification set. The union base carries only the
/// identity/dedup key (<see cref="NotificationId"/>) and display data — it does not assume
/// the notification is chat-related.
/// </summary>
[RpcSerializable]
[DataContract, MessagePackObject]
[Union(1, typeof(MessageNotification))]
[Union(2, typeof(ReplyNotification))]
[Union(3, typeof(InvitationNotification))]
[Union(4, typeof(MentionNotification))]
[Union(5, typeof(ReactionNotification))]
[Union(6, typeof(AttentionNotification))]
[Union(7, typeof(ThreadNotification))]
[Union(8, typeof(ConversationNotification))]
[Union(9, typeof(CallNotification))]
public abstract partial record Notification(
    [property: DataMember(Order = 0), Key(0)] NotificationId Id,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, ISanitized
{
    [DataMember(Order = 2), Key(2)]
    public string Title {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember(Order = 3), Key(3)]
    public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember(Order = 4), Key(4)]
    public string IconUrl { get; init; } = "";
    [DataMember(Order = 5), Key(5)]
    public Moment CreatedAt { get; init; }
    [DataMember(Order = 6), Key(6)]
    public Moment SentAt { get; init; }
    // Key 7 held HandledAt: dismissal removes the notification from UserNotificationInfo.Items
    // rather than stamping it, so the field was always null. Keys are wire format - it stays vacant.
    // Optional call-to-action buttons (empty for ordinary chat notifications). Key 16 avoids the
    // 8..15 range that subtypes use, so it's collision-free across the whole union.
    [DataMember(Order = 16), Key(16)]
    public ApiArray<NotificationAction> Actions { get; init; }

    // Policy - per-kind, overridden by subtypes. Both are computed rather than stored, so they
    // apply to blobs written before they existed.
    // Explicit is the safe default: the subtypes that opt into OnRead are exactly the ones
    // GetReadAnchor can resolve, so a kind with no anchor can't be filtered by a read position
    // that doesn't apply to it.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public virtual NotificationDismissMode DismissMode => NotificationDismissMode.Explicit;
    // Null = never expires. Deliberately has no importance term: a ringer is the *most* expirable
    // kind, not the least, since nothing else can clear it.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public virtual Moment? ExpiresAt => null;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;

    public Notification WithSimilar(Notification similar)
    {
        if (Id != similar.Id)
            throw new ArgumentOutOfRangeException(nameof(similar));

        return this with {
            Version = similar.Version,
            CreatedAt = similar.CreatedAt,
        };
    }

    // Merges this incoming notification with the one it replaces (null = first for its key). The
    // base keeps only the identity carry-over; subtypes that coalesce accumulate their state here.
    public virtual Notification MergeWith(Notification? existing)
    {
        if (existing is null)
            return this;

        // A redelivered (same SentAt) or out-of-order older event carries no new content: return the
        // existing instance unchanged so the reconcile's reference-equality check skips a duplicate
        // push. Without this an at-least-once redelivery re-alerts — an incoming call would re-ring.
        // Coalescing kinds override this with their own idempotent merge.
        if (SentAt <= existing.SentAt)
            return existing;
        return WithSimilar(existing);
    }
}
