using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 ChatEntry with original MemoryPack orders for backward compatibility.
/// Remove once all clients are migrated past v2.6.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
[Obsolete("2025.03: Legacy v2.6 compat type")]
public sealed partial record LegacyChatEntry(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>
{
    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(13), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackClientSideBeginsAt {
        get => ClientSideBeginsAt;
        init => ClientSideBeginsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(14), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(15), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackContentEndsAt {
        get => ContentEndsAt;
        init => ContentEndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(20), IgnoreMember]
    private ApiNullable8<long> MemoryPackAudioEntryId {
        get => AudioEntryLid;
        init => AudioEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(21), IgnoreMember]
    private ApiNullable8<long> MemoryPackVideoEntryId {
        get => VideoEntryLid;
        init => VideoEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(23), IgnoreMember]
    private ApiNullable8<long> MemoryPackRepliedEntryLocalId {
        get => RepliedEntryLid;
        init => RepliedEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(26), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackForwardedChatEntryBeginsAt {
        get => ForwardedChatEntryBeginsAt;
        init => ForwardedChatEntryBeginsAt = value;
    }

    #endregion

    [DataMember(Order = 10), MemoryPackOrder(10), Key(2)] public bool IsRemoved { get; init; }
    [DataMember(Order = 11), MemoryPackOrder(11), Key(3)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 12), MemoryPackOrder(12), Key(4)] public Moment BeginsAt { get; init; }
    [DataMember(Order = 13), MemoryPackIgnore, Key(5)] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember(Order = 14), MemoryPackIgnore, Key(6)] public Moment? EndsAt { get; init; }
    [DataMember(Order = 15), MemoryPackIgnore, Key(7)] public Moment? ContentEndsAt { get; init; }
    [DataMember(Order = 16), MemoryPackOrder(16), Key(8)] public string Content { get; init; } = "";
    [DataMember(Order = 32), MemoryPackOrder(32), Key(23)] public HashString ContentHash { get; init; }
    [DataMember(Order = 17), MemoryPackOrder(17), Key(9)] public SystemEntry? SystemEntry { get; init; }
    [DataMember(Order = 18), MemoryPackOrder(18), Key(10)] public bool HasReactions { get; init; }
    [DataMember(Order = 19), MemoryPackOrder(19), Key(11)] public string StreamId { get; init; } = "";
    [DataMember(Order = 20), MemoryPackIgnore, Key(12)] public long? AudioEntryLid { get; init; }
    [DataMember(Order = 21), MemoryPackIgnore, Key(13)] public long? VideoEntryLid { get; init; }
    [DataMember(Order = 37), MemoryPackOrder(37), Key(28)] public string? AudioId { get; init; }
    [DataMember(Order = 22), MemoryPackOrder(22), Key(14)] public LinearMap TimeMap { get; init; }
    [DataMember(Order = 23), MemoryPackIgnore, Key(15)] public long? RepliedEntryLid { get; init; }
    [DataMember(Order = 24), MemoryPackOrder(24), Key(16)] public ChatEntryId? ForwardedChatEntryId { get; init; }
    [DataMember(Order = 25), MemoryPackOrder(25), Key(17)] public AuthorId? ForwardedAuthorId { get; init; }
    [DataMember(Order = 26), MemoryPackIgnore, Key(18)] public Moment? ForwardedChatEntryBeginsAt { get; init; }
    [DataMember(Order = 27), MemoryPackOrder(27), Key(19)] public string? ForwardedChatTitle { get; init; }
    [DataMember(Order = 28), MemoryPackOrder(28), Key(20)] public string? ForwardedAuthorName { get; init; }
    [DataMember(Order = 31), MemoryPackOrder(31), Key(22)] public Symbol[] LinkPreviewIds { get; init; } = [];
    [DataMember(Order = 30), MemoryPackOrder(30), Key(21)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember(Order = 33), MemoryPackOrder(33), Key(24)] public bool IsThreadStartEntry { get; init; }
    [DataMember(Order = 34), MemoryPackOrder(34), Key(25)] public bool IsThreadEntry { get; init; }
    [DataMember(Order = 35), MemoryPackOrder(35), Key(26)] public bool HasAttachmentUploads { get; init; }
    [DataMember(Order = 36), MemoryPackOrder(36), Key(27)] public string ClientId { get; init; } = "";

    // Populated only on reads
    [DataMember(Order = 50), MemoryPackOrder(50), Key(29)] public ChatEntryAttachment[] Attachments { get; init; } = [];
    [DataMember(Order = 52), MemoryPackOrder(52), Key(30)] public LinkPreview[] LinkPreviews { get; init; } = [];
    [DataMember(Order = 53), MemoryPackOrder(53), Key(31)] public ChatEntryAttachment[] AttachmentUploads { get; init; } = [];

    [MemoryPackConstructor, SerializationConstructor]
    public LegacyChatEntry() : this((ChatEntryId)null!) { }

    public static LegacyChatEntry From(ChatEntry entry)
        => new(entry.Id, entry.Version) {
            IsRemoved = entry.IsRemoved,
            AuthorId = entry.AuthorId,
            BeginsAt = entry.BeginsAt,
            ClientSideBeginsAt = entry.Audio?.ClientSideBeginsAt,
            EndsAt = entry.EndsAt,
            ContentEndsAt = entry.Audio?.ContentEndsAt,
            Content = entry.Content,
            ContentHash = entry.ContentHash,
            SystemEntry = entry.SystemEntry,
            HasReactions = entry.HasReactions,
            StreamId = entry.ContentStreamId,
            AudioEntryLid = null, // No longer tracked separately
            VideoEntryLid = null,
            AudioId = entry.Audio?.BlobId,
            TimeMap = entry.Audio?.TimeMap ?? default,
            RepliedEntryLid = entry.RepliedEntryLid,
            ForwardedChatEntryId = entry.Forwarded?.ChatEntryId,
            ForwardedAuthorId = entry.Forwarded?.AuthorId,
            ForwardedChatEntryBeginsAt = entry.Forwarded?.BeginsAt,
            ForwardedChatTitle = entry.Forwarded?.ChatTitle,
            ForwardedAuthorName = entry.Forwarded?.AuthorName,
            LinkPreviewIds = entry.LinkPreviewIds,
            LinkPreviewMode = entry.LinkPreviewMode,
            IsThreadStartEntry = entry.IsThreadStart,
            IsThreadEntry = entry.IsThread,
            HasAttachmentUploads = entry.HasUploadingAttachments,
            ClientId = entry.ClientId,
            Attachments = entry.Attachments,
            LinkPreviews = entry.LinkPreviews,
            AttachmentUploads = [],
        };
}
