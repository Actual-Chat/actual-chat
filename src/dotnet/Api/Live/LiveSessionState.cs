using ActualChat.Chat;

namespace ActualChat.Live;

/// <summary>
/// The persisted per-chat live-session state: call/lifecycle facts (host, rules, the 2+ peer latch,
/// closing) plus the in-progress transcript block. The chat-view block is its <see cref="ToConversation"/>
/// projection, surfaced only once the session latches.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveSessionState
{
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public long StartEntryLid { get; init; }
    [DataMember(Order = 2), Key(2)]
    public long EndEntryLid { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 5), Key(5)]
    public string Title { get; init; } = "";
    [DataMember(Order = 6), Key(6)]
    public string Description { get; init; } = "";
    [DataMember(Order = 7), Key(7)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 8), Key(8)]
    public bool TranscriptionOn { get; init; }
    [DataMember(Order = 9), Key(9)]
    public int MessageCount { get; init; }
    [DataMember(Order = 10), Key(10)]
    public Moment LastSummaryAt { get; init; }
    [DataMember(Order = 11), Key(11)]
    public bool StartNotificationSent { get; init; }
    [DataMember(Order = 12), Key(12)]
    public long Version { get; init; }
    [DataMember(Order = 13), Key(13)]
    public bool IsClosing { get; init; }
    [DataMember(Order = 14), Key(14)]
    public Moment? ClosingAt { get; init; }
    [DataMember(Order = 15), Key(15)]
    public AuthorId Host { get; init; } = null!;
    [DataMember(Order = 16), Key(16)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    [DataMember(Order = 17), Key(17)]
    public Moment? SessionStartedAt { get; init; }
    [DataMember(Order = 18), Key(18)]
    public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
    [DataMember(Order = 19), Key(19)]
    public long VisibleStartLid { get; init; }
    [DataMember(Order = 20), Key(20)]
    public long ContextStartLid { get; init; }
    [DataMember(Order = 21), Key(21)]
    public bool IsExpandedByDefault { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long EffectiveVisibleStartLid => VisibleStartLid > 0 ? VisibleStartLid : StartEntryLid;
    [IgnoreDataMember, IgnoreMember]
    public Range<long> VisibleEntryLidRange
        => new(EffectiveVisibleStartLid, Math.Max(EndEntryLid, EffectiveVisibleStartLid) + 1);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ConversationId ConversationId => ConversationId.New(ChatId, EffectiveVisibleStartLid);
    // Ring id must stay fixed for the whole call, unlike ConversationId, which jumps at the latch.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ConversationId RingConversationId => ConversationId.New(ChatId, StartEntryLid);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsCall => Kind is LiveSessionKind.Call or LiveSessionKind.Dialing;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsDialing => Kind == LiveSessionKind.Dialing;

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
