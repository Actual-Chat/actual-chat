using ActualChat.Compliance;
using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// A single notification in a user's notification set. The union base carries only the
/// identity/dedup key (<see cref="NotificationId"/>) and display text — it does not assume
/// the notification is chat-related.
/// </summary>
[RpcSerializable]
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
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasId<NotificationId>, IHasVersion<long>, ISanitized
{
    [DataMember(Order = 2), Key(2)]
    public string Title { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 3), Key(3)]
    public string Text { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 6), Key(6)]
    public string IconUrl { get; init; } = "";
    [DataMember(Order = 7), Key(7)]
    public Moment CreatedAt { get; init; }
    [DataMember(Order = 8), Key(8)]
    public Moment SentAt { get; init; }
    [DataMember(Order = 9), Key(9)]
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

    public static NotificationItem New(
        NotificationId id,
        ChatId chatId,
        long entryLid = 0,
        AuthorId? authorId = null)
        => id.Kind switch {
            NotificationKind.Message => new MessageNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.Reply => new ReplyNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.Invitation => new InvitationNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.Mention => new MentionNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.Reaction => new ReactionNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.Attention => new AttentionNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            NotificationKind.NewThread => new NewThreadNotificationItem(id) { ChatId = chatId, EntryLid = entryLid, AuthorId = authorId },
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    public NotificationItem WithSimilar(NotificationItem similar)
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

[DataContract]
public abstract partial record ChatNotificationItem(
    NotificationId Id,
    long Version = 0
    ) : NotificationItem(Id, Version)
{
    [DataMember(Order = 4), Key(4)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 5), Key(5)]
    public long EntryLid { get; init; }
    [DataMember(Order = 10), Key(10)]
    public AuthorId? AuthorId { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId EntryId => ChatEntryId.New(ChatId, EntryLid);
}

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record MessageNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReplyNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record InvitationNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record MentionNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReactionNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record AttentionNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record NewThreadNotificationItem(
    NotificationId Id, long Version = 0
    ) : ChatNotificationItem(Id, Version);
