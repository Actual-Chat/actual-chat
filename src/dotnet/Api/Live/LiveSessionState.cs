using ActualChat.Chat;

namespace ActualChat.Live;

/// <summary>
/// The persisted per-chat live-session state: call/lifecycle facts (host, rules, the 2+ peer latch,
/// closing) plus the in-progress transcript block. The chat-view block is its <see cref="ToConversation"/>
/// projection, surfaced only once the session latches.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LiveSessionState
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long StartEntryLid { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long EndEntryLid { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public string Title { get; init; } = "";
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public string Description { get; init; } = "";
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public bool TranscriptionOn { get; init; }
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)]
    public int MessageCount { get; init; }
    [DataMember(Order = 10), MemoryPackOrder(10), Key(10)]
    public Moment LastSummaryAt { get; init; }
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)]
    public bool StartNotificationSent { get; init; }
    [DataMember(Order = 12), MemoryPackOrder(12), Key(12)]
    public long Version { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13), Key(13)]
    public bool IsClosing { get; init; }
    [DataMember(Order = 14), MemoryPackOrder(14), Key(14)]
    public Moment? ClosingAt { get; init; }
    [DataMember(Order = 15), MemoryPackOrder(15), Key(15)]
    public AuthorId Host { get; init; } = null!;
    [DataMember(Order = 16), MemoryPackOrder(16), Key(16)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    [DataMember(Order = 17), MemoryPackOrder(17), Key(17)]
    public Moment? SessionStartedAt { get; init; }
    [DataMember(Order = 18), MemoryPackOrder(18), Key(18)]
    public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
    [DataMember(Order = 19), MemoryPackOrder(19), Key(19)]
    public long VisibleStartLid { get; init; }
    [DataMember(Order = 20), MemoryPackOrder(20), Key(20)]
    public long ContextStartLid { get; init; }
    [DataMember(Order = 21), MemoryPackOrder(21), Key(21)]
    public bool IsExpandedByDefault { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long EffectiveVisibleStartLid => VisibleStartLid > 0 ? VisibleStartLid : StartEntryLid;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Range<long> VisibleEntryLidRange
        => new(EffectiveVisibleStartLid, Math.Max(EndEntryLid, EffectiveVisibleStartLid) + 1);
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ConversationId ConversationId => ConversationId.New(ChatId, EffectiveVisibleStartLid);

    public Conversation ToConversation()
        => new(ConversationId, Version) {
            Title = Title,
            Description = Description,
            Summary = Summary,
            EndEntryLid = Math.Max(EndEntryLid, EffectiveVisibleStartLid),
            StartsAt = StartedAt,
            EndsAt = LastSummaryAt == default ? StartedAt : LastSummaryAt,
            MessageCount = MessageCount,
            AuthorIds = AuthorIds,
            IsExpandedByDefault = IsExpandedByDefault,
        };

    public Conversation ToMaterializedConversation()
        => new(ConversationId.New(ChatId, ContextStartLid > 0 ? ContextStartLid : EffectiveVisibleStartLid), Version) {
            Title = Title,
            Description = Description,
            Summary = Summary,
            EndEntryLid = EndEntryLid,
            StartsAt = StartedAt,
            EndsAt = LastSummaryAt == default ? StartedAt : LastSummaryAt,
            MessageCount = MessageCount,
            AuthorIds = AuthorIds,
            IsExpandedByDefault = IsExpandedByDefault,
        };
}
