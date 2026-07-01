using ActualChat.Compliance;
using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// A single notification in a user's notification set. The union base carries only the
/// identity/dedup key (<see cref="NotificationId"/>) and display data — it does not assume
/// the notification is chat-related.
/// </summary>
[RpcSerializable]
[DataContract]
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
    public string Title { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 3), Key(3)]
    public string Text { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 4), Key(4)]
    public string IconUrl { get; init; } = "";
    [DataMember(Order = 5), Key(5)]
    public Moment CreatedAt { get; init; }
    [DataMember(Order = 6), Key(6)]
    public Moment SentAt { get; init; }
    [DataMember(Order = 7), Key(7)]
    public Moment? HandledAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public NotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsActive => HandledAt == null;

    public Notification WithSimilar(Notification similar)
    {
        if (Id != similar.Id)
            throw new ArgumentOutOfRangeException(nameof(similar));

        return this with {
            Version = similar.Version,
            CreatedAt = similar.CreatedAt,
            HandledAt = null,
        };
    }
}
