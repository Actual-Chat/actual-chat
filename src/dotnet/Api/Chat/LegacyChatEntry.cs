using ActualChat.Comparison;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Wire-frozen v2.7 ChatEntry shape kept for clients that still talk MemoryPack.
/// Routed via <c>ILegacyChats</c>; never used internally outside conversion helpers.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record LegacyChatEntry(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>
{
    // Flags
    [DataMember(Order = 2), MemoryPackOrder(2)] public ChatEntryFlags Flags { get; init; }

    // Author & Timing
    [DataMember(Order = 3), MemoryPackOrder(3)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 4), MemoryPackOrder(4)] public Moment BeginsAt { get; init; }
    [DataMember(Order = 5), MemoryPackIgnore] public Moment? EndsAt { get; init; }
    // Content
    [DataMember(Order = 6), MemoryPackOrder(6)] public string Content { get; init; } = "";
    [DataMember(Order = 7), MemoryPackOrder(7)] public HashString ContentHash { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8)] public LegacySystemEntry? SystemEntry { get; init; }
    [DataMember(Order = 9), MemoryPackOrder(9)] public string ContentStreamId { get; init; } = "";
    // Reply
    [DataMember(Order = 10), MemoryPackIgnore] public long? RepliedEntryLid { get; init; }
    // Forward
    [DataMember(Order = 11), MemoryPackOrder(11)] public ChatEntryForwarded? Forwarded { get; init; }
    // Audio
    [DataMember(Order = 12), MemoryPackOrder(12)] public ChatEntryAudio? Audio { get; init; }
    // Links
    [DataMember(Order = 13), MemoryPackOrder(13)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember(Order = 14), MemoryPackOrder(14)] public Symbol[] LinkPreviewIds { get; init; } = [];
    [DataMember(Order = 15), MemoryPackOrder(15)] public LinkPreview[] LinkPreviews { get; init; } = [];

    // Client
    [DataMember(Order = 16), MemoryPackOrder(16)] public string ClientId { get; init; } = "";

    // Read-only (populated on reads)
    [DataMember(Order = 17), MemoryPackOrder(17)] public ChatEntryAttachment[] Attachments { get; init; } = [];

    // MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(5)]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [MemoryPackInclude, MemoryPackOrder(10)]
    private ApiNullable8<long> MemoryPackRepliedEntryLocalId {
        get => RepliedEntryLid;
        init => RepliedEntryLid = value;
    }

    [MemoryPackConstructor]
    public LegacyChatEntry() : this((ChatEntryId)null!) { }

    // This record relies on referential equality
    public bool Equals(LegacyChatEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public static LegacyChatEntry From(ChatEntry entry)
        => new(entry.Id, entry.Version) {
            Flags = entry.Flags,
            AuthorId = entry.AuthorId,
            BeginsAt = entry.BeginsAt,
            EndsAt = entry.EndsAt,
            Content = entry.Content,
            ContentHash = entry.ContentHash,
            SystemEntry = LegacySystemEntry.From(entry),
            ContentStreamId = entry.ContentStreamId,
            RepliedEntryLid = entry.RepliedEntryLid,
            Forwarded = entry.Forwarded,
            Audio = entry.Audio,
            LinkPreviewMode = entry.LinkPreviewMode,
            LinkPreviewIds = entry.LinkPreviewIds,
            LinkPreviews = entry.LinkPreviews,
            ClientId = entry.ClientId,
            Attachments = entry.Attachments,
        };
}
