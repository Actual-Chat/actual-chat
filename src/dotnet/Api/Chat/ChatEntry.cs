using ActualChat.Comparison;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a message or media entry in a chat conversation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatEntry(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget, ISanitized
{
    public static readonly VersionEqualityComparer<ChatEntry, ChatEntryId> VersionEqualityComparer = new();

    public static readonly Requirement<ChatEntry> MustExist = Requirement.New(
        (ChatEntry? c) => c?.Id is not null,
        new(() => StandardError.NotFound<ChatEntry>()));
    public static readonly Requirement<ChatEntry> MustNotBeRemoved = Requirement.New(
        (ChatEntry? c) => c?.Id is not null && !c.IsRemoved,
        new(() => StandardError.NotFound<ChatEntry>()));

    public static ChatEntry Removed(ChatEntryId id)
        => new (id) { Flags = ChatEntryFlags.IsRemoved };
    public static ChatEntry Loading(ChatEntryId id)
        => new (id, -1);

    // Flags
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)] public ChatEntryFlags Flags { get; init; }

    // Author & Timing
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)] public Moment BeginsAt { get; init; }
    [DataMember(Order = 5), MemoryPackIgnore, Key(5)] public Moment? EndsAt { get; init; }
    // Content
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)] public string Content { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)] public HashString ContentHash { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)] public SystemEntry? SystemEntry { get; init; }
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)] public string ContentStreamId { get; init; } = "";
    // Reply
    [DataMember(Order = 10), MemoryPackIgnore, Key(10)] public long? RepliedEntryLid { get; init; }
    // Forward
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)] public ChatEntryForwarded? Forwarded { get; init; }
    // Audio
    [DataMember(Order = 12), MemoryPackOrder(12), Key(12)] public ChatEntryAudio? Audio { get; init; }
    // Links
    [DataMember(Order = 13), MemoryPackOrder(13), Key(13)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember(Order = 14), MemoryPackOrder(14), Key(14)] public Symbol[] LinkPreviewIds { get; init; } = [];
    [DataMember(Order = 15), MemoryPackOrder(15), Key(15)] public LinkPreview[] LinkPreviews { get; init; } = [];

    // Client
    [DataMember(Order = 16), MemoryPackOrder(16), Key(16)] public string ClientId { get; init; } = ""; // Soon obsolete

    // Read-only (populated on reads)
    [DataMember(Order = 17), MemoryPackOrder(17), Key(17)] public ChatEntryAttachment[] Attachments { get; init; } = [];

    // MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(5), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(10), IgnoreMember]
    private ApiNullable8<long> MemoryPackRepliedEntryLocalId {
        get => RepliedEntryLid;
        init => RepliedEntryLid = value;
    }

    // Used on the client side only.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string ClientUid { get; init; } = "";
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public object? SendingTag { get; init; }

    // Bool properties — computed from Flags
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsRemoved {
        get => Flags.HasFlag(ChatEntryFlags.IsRemoved);
        init => Flags = value ? Flags | ChatEntryFlags.IsRemoved : Flags & ~ChatEntryFlags.IsRemoved;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool HasReactions {
        get => Flags.HasFlag(ChatEntryFlags.HasReactions);
        init => Flags = value ? Flags | ChatEntryFlags.HasReactions : Flags & ~ChatEntryFlags.HasReactions;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsThreadStart {
        get => Flags.HasFlag(ChatEntryFlags.IsThreadStart);
        init => Flags = value ? Flags | ChatEntryFlags.IsThreadStart : Flags & ~ChatEntryFlags.IsThreadStart;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsThread {
        get => Flags.HasFlag(ChatEntryFlags.IsThread);
        init => Flags = value ? Flags | ChatEntryFlags.IsThread : Flags & ~ChatEntryFlags.IsThread;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool HasUploadingAttachments {
        get => Flags.HasFlag(ChatEntryFlags.HasUploadingAttachments);
        init => Flags = value ? Flags | ChatEntryFlags.HasUploadingAttachments : Flags & ~ChatEntryFlags.HasUploadingAttachments;
    }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsContentStreaming => !ContentStreamId.IsNullOrEmpty();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsSystemEntry => SystemEntry != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool HasAudio => Audio != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool HasMarkup => !IsSystemEntry && !HasAudio;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsSending => SendingTag is not null;

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntry() : this((ChatEntryId)null!) { }

    // This record relies on referential equality
    public bool Equals(ChatEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool VersionEquals(ChatEntry? other) => VersionEqualityComparer.Equals(this, other);
}

/// <summary>
/// Represents changes to a <see cref="ChatEntry"/> for incremental updates.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method: MemoryPackConstructor, SerializationConstructor]
public sealed partial record ChatEntryDiff() : RecordDiff, ISanitized
{
    [DataMember, MemoryPackOrder(0)] public bool? IsRemoved { get; init; }
    [DataMember, MemoryPackOrder(1)] public AuthorId? AuthorId { get; init; }
    [DataMember, MemoryPackOrder(2)] public Moment? BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public Option<Moment?> EndsAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public string? Content { get => Sanitizer.MaskPrivate(field); init; }
    [DataMember, MemoryPackOrder(5)] public Option<SystemEntry?> SystemEntry { get; init; }
    [DataMember, MemoryPackOrder(6)] public string? ContentStreamId { get; init; }
    [DataMember, MemoryPackOrder(7)] public Option<ChatEntryAudio?> Audio { get; init; }
    [DataMember, MemoryPackOrder(8)] public Option<ChatEntryForwarded?> Forwarded { get; init; }
    [DataMember, MemoryPackOrder(9)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember, MemoryPackOrder(10)] public ChatEntryAttachment[]? Attachments { get; init; }
    [DataMember, MemoryPackOrder(11)] public LinkPreviewMode? LinkPreviewMode { get; init; }
    [DataMember, MemoryPackOrder(12)] public bool? HasReactions { get; init; }
    [DataMember, MemoryPackOrder(13)] public bool? IsThreadStart { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool? IsThread { get; init; }
    [DataMember, MemoryPackOrder(15)] public string? ClientId { get; init; } // Soon obsolete

    public ChatEntryDiff(ChatEntry entry) : this()
    {
        AuthorId = entry.AuthorId;
        BeginsAt = entry.BeginsAt;
        EndsAt = entry.EndsAt;
        Content = entry.Content;
        SystemEntry = entry.SystemEntry;
        ContentStreamId = entry.ContentStreamId;
        Audio = entry.Audio;
        RepliedEntryLid = entry.RepliedEntryLid;
        Forwarded = entry.Forwarded;
        Attachments = entry.Attachments;
        LinkPreviewMode = entry.LinkPreviewMode;
        // IsXxx
        IsRemoved = entry.IsRemoved;
        IsThreadStart = entry.IsThreadStart;
        IsThread = entry.IsThread;
        HasReactions = entry.HasReactions;
        // Soon obsolete
        ClientId = entry.ClientId;
    }
}
