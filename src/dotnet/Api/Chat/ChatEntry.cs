using ActualChat.Comparison;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Abstract message or media entry in a chat. Concrete kinds are <see cref="TextEntry"/>
/// (regular messages) and <see cref="SystemEntry"/> descendants (system events).
/// </summary>
[RpcSerializable]
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
[Union(0, typeof(TextEntry))]
[Union(1, typeof(MembersChangedEntry))]
[Union(2, typeof(NotifyMembersEntry))]
public abstract partial record ChatEntry(
    [property: DataMember(Order = 0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
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
        => new TextEntry(id) { Flags = ChatEntryFlags.IsRemoved };
    public static ChatEntry Loading(ChatEntryId id)
        => new TextEntry(id, -1);

    public static ChatEntry NewEmpty(ChatEntryId id, ChatEntryKind kind = ChatEntryKind.Text)
        => kind switch {
            ChatEntryKind.MembersChanged => new MembersChangedEntry(id),
            ChatEntryKind.NotifyMembers => new NotifyMembersEntry(id),
            _ => new TextEntry(id),
        };

    // Flags
    [DataMember(Order = 2), Key(2)] public ChatEntryFlags Flags { get; init; }

    // Author & Timing
    [DataMember(Order = 3), Key(3)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 4), Key(4)] public Moment BeginsAt { get; init; }
    [DataMember(Order = 5), Key(5)] public Moment? EndsAt { get; init; }
    // Content
    [DataMember(Order = 6), Key(6)] public string Content { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 7), Key(7)] public HashString ContentHash { get; init; }
    [DataMember(Order = 8), Key(8)] public string ContentStreamId { get; init; } = "";
    // Reply
    [DataMember(Order = 9), Key(9)] public long? RepliedEntryLid { get; init; }
    // Forward
    [DataMember(Order = 10), Key(10)] public ChatEntryForwarded? Forwarded { get; init; }
    // Audio
    [DataMember(Order = 11), Key(11)] public ChatEntryAudio? Audio { get; init; }
    // Links
    [DataMember(Order = 12), Key(12)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember(Order = 13), Key(13)] public Symbol[] LinkPreviewIds { get; init; } = [];
    [DataMember(Order = 14), Key(14)] public LinkPreview[] LinkPreviews { get; init; } = [];

    // Location
    [DataMember(Order = 17), Key(17)] public SharedLocationId? LocationId { get; init; }

    // Client
    [DataMember(Order = 15), Key(15)] public string ClientId { get; init; } = ""; // Soon obsolete

    // Read-only (populated on reads)
    [DataMember(Order = 16), Key(16)] public ChatEntryAttachment[] Attachments { get; init; } = [];

    // Used on the client side only.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string ClientUid { get; init; } = "";
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public object? SendingTag { get; init; }

    // Bool properties — computed from Flags
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsRemoved {
        get => Flags.HasFlag(ChatEntryFlags.IsRemoved);
        init => Flags = value ? Flags | ChatEntryFlags.IsRemoved : Flags & ~ChatEntryFlags.IsRemoved;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasReactions {
        get => Flags.HasFlag(ChatEntryFlags.HasReactions);
        init => Flags = value ? Flags | ChatEntryFlags.HasReactions : Flags & ~ChatEntryFlags.HasReactions;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsThreadStart {
        get => Flags.HasFlag(ChatEntryFlags.IsThreadStart);
        init => Flags = value ? Flags | ChatEntryFlags.IsThreadStart : Flags & ~ChatEntryFlags.IsThreadStart;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsThread {
        get => Flags.HasFlag(ChatEntryFlags.IsThread);
        init => Flags = value ? Flags | ChatEntryFlags.IsThread : Flags & ~ChatEntryFlags.IsThread;
    }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasUploadingAttachments {
        get => Flags.HasFlag(ChatEntryFlags.HasUploadingAttachments);
        init => Flags = value ? Flags | ChatEntryFlags.HasUploadingAttachments : Flags & ~ChatEntryFlags.HasUploadingAttachments;
    }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsSystemEntry => this is SystemEntry;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsContentStreaming => !ContentStreamId.IsNullOrEmpty();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasAudio => Audio != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasLocation => LocationId != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasMarkup => this is not SystemEntry && !HasAudio;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsSending => SendingTag is not null;

    // This record relies on referential equality
    public virtual bool Equals(ChatEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool VersionEquals(ChatEntry? other) => VersionEqualityComparer.Equals(this, other);
}

/// <summary>
/// Diff-style payload used by <c>ChatsBackend_ChangeEntry</c> to create or update a
/// <see cref="ChatEntry"/>. <see cref="Kind"/> selects the concrete entry type to create
/// when the change is a Create; on Update the existing concrete type is preserved and
/// only matching fields are applied.
/// </summary>
[DataContract, MessagePackObject(true)]
public sealed partial record ChatEntryDiff() : RecordDiff, ISanitized
{
    [DataMember] public ChatEntryKind? Kind { get; init; }
    [DataMember] public bool? IsRemoved { get; init; }
    [DataMember] public AuthorId? AuthorId { get; init; }
    [DataMember] public Moment? BeginsAt { get; init; }
    [DataMember] public Option<Moment?> EndsAt { get; init; }
    [DataMember] public string? Content { get => Sanitizer.MaskPrivate(field); init; }
    [DataMember] public string? ContentStreamId { get; init; }
    [DataMember] public Option<ChatEntryAudio?> Audio { get; init; }
    [DataMember] public Option<ChatEntryForwarded?> Forwarded { get; init; }
    [DataMember] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember] public ChatEntryAttachment[]? Attachments { get; init; }
    [DataMember] public SharedLocationId? LocationId { get; init; }
    [DataMember] public LinkPreviewMode? LinkPreviewMode { get; init; }
    [DataMember] public bool? HasReactions { get; init; }
    [DataMember] public bool? IsThreadStart { get; init; }
    [DataMember] public bool? IsThread { get; init; }
    [DataMember] public string? ClientId { get; init; } // Soon obsolete

    // System-entry payload (applied when the target is a SystemEntry descendant)
    [DataMember] public AuthorId? TargetAuthorId { get; init; }
    [DataMember] public string? TargetAuthorName { get; init; }
    [DataMember] public bool? HasLeft { get; init; }

    public ChatEntryDiff(ChatEntry entry) : this()
    {
        Kind = entry switch {
            MembersChangedEntry => ChatEntryKind.MembersChanged,
            NotifyMembersEntry => ChatEntryKind.NotifyMembers,
            _ => ChatEntryKind.Text,
        };
        AuthorId = entry.AuthorId;
        BeginsAt = entry.BeginsAt;
        EndsAt = entry.EndsAt;
        Content = entry.Content;
        ContentStreamId = entry.ContentStreamId;
        Audio = entry.Audio;
        RepliedEntryLid = entry.RepliedEntryLid;
        Forwarded = entry.Forwarded;
        Attachments = entry.Attachments;
        LocationId = entry.LocationId;
        LinkPreviewMode = entry.LinkPreviewMode;
        IsRemoved = entry.IsRemoved;
        IsThreadStart = entry.IsThreadStart;
        IsThread = entry.IsThread;
        HasReactions = entry.HasReactions;
        ClientId = entry.ClientId;

        switch (entry) {
        case MembersChangedEntry mc:
            TargetAuthorId = mc.TargetAuthorId;
            TargetAuthorName = mc.TargetAuthorName;
            HasLeft = mc.HasLeft;
            break;
        case NotifyMembersEntry nm:
            TargetAuthorId = nm.TargetAuthorId;
            TargetAuthorName = nm.TargetAuthorName;
            break;
        }
    }
}

public enum ChatEntryKind {
    Text = 0,
    MembersChanged = 1,
    NotifyMembers = 2,
}
